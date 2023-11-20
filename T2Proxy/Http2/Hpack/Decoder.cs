

using System;
using System.IO;
using T2Proxy.Models;

namespace T2Proxy.Http2.Hpack;

internal class Decoder
{
    private readonly DynamicTable dynamicTable;

    private readonly int maxHeaderSize;
    private int encoderMaxDynamicTableSize;

    private long headerSize;
    private bool huffmanEncoded;
    private int index;
    private HpackUtil.IndexType indexType;
    private int maxDynamicTableSize;
    private bool maxDynamicTableSizeChangeRequired;
    private ByteStream name;
    private int nameLength;
    private int skipLength;
    private State state;
    private int valueLength;

    public Decoder(int maxHeaderSize, int maxHeaderTableSize)
    {
        dynamicTable = new DynamicTable(maxHeaderTableSize);
        this.maxHeaderSize = maxHeaderSize;
        maxDynamicTableSize = maxHeaderTableSize;
        encoderMaxDynamicTableSize = maxHeaderTableSize;
        maxDynamicTableSizeChangeRequired = false;
        Reset();
    }

    private void Reset()
    {
        headerSize = 0;
        state = State.ReadHeaderRepresentation;
        indexType = HpackUtil.IndexType.None;
    }

    public void Decode(BinaryReader input, IHeaderListener headerListener)
    {
        while (input.BaseStream.Length - input.BaseStream.Position > 0)
            switch (state)
            {
                case State.ReadHeaderRepresentation:
                    var b = input.ReadSByte();
                    if (maxDynamicTableSizeChangeRequired && (b & 0xE0) != 0x20)
                   
                        throw new IOException("max dynamic table size change required");

                    if (b < 0)
                    {
                        index = b & 0x7F;
                        if (index == 0)
                            throw new IOException("illegal index value (" + index + ")");
                        if (index == 0x7F)
                            state = State.ReadIndexedHeader;
                        else
                            IndexHeader(index, headerListener);
                    }
                    else if ((b & 0x40) == 0x40)
                    {
                        indexType = HpackUtil.IndexType.Incremental;
                        index = b & 0x3F;
                        if (index == 0)
                        {
                            state = State.ReadLiteralHeaderNameLengthPrefix;
                        }
                        else if (index == 0x3F)
                        {
                            state = State.ReadIndexedHeaderName;
                        }
                        else
                        {
                            ReadName(index);
                            state = State.ReadLiteralHeaderValueLengthPrefix;
                        }
                    }
                    else if ((b & 0x20) == 0x20)
                    {
                        
                        index = b & 0x1F;
                        if (index == 0x1F)
                        {
                            state = State.ReadMaxDynamicTableSize;
                        }
                        else
                        {
                            SetDynamicTableSize(index);
                            state = State.ReadHeaderRepresentation;
                        }
                    }
                    else
                    {
                        indexType = (b & 0x10) == 0x10 ? HpackUtil.IndexType.Never : HpackUtil.IndexType.None;
                        index = b & 0x0F;
                        if (index == 0)
                        {
                            state = State.ReadLiteralHeaderNameLengthPrefix;
                        }
                        else if (index == 0x0F)
                        {
                            state = State.ReadIndexedHeaderName;
                        }
                        else
                        {
                            ReadName(index);
                            state = State.ReadLiteralHeaderValueLengthPrefix;
                        }
                    }

                    break;

                case State.ReadMaxDynamicTableSize:
                    var maxSize = DecodeUle128(input);
                    if (maxSize == -1) return;

                    if (maxSize > int.MaxValue - index) throw new IOException("decompression failure");

                    SetDynamicTableSize(index + maxSize);
                    state = State.ReadHeaderRepresentation;
                    break;

                case State.ReadIndexedHeader:
                    var headerIndex = DecodeUle128(input);
                    if (headerIndex == -1) return;

                    if (headerIndex > int.MaxValue - index) throw new IOException("decompression failure");

                    IndexHeader(index + headerIndex, headerListener);
                    state = State.ReadHeaderRepresentation;
                    break;

                case State.ReadIndexedHeaderName:
                    var nameIndex = DecodeUle128(input);
                    if (nameIndex == -1) return;

                    if (nameIndex > int.MaxValue - index) throw new IOException("decompression failure");

                    ReadName(index + nameIndex);
                    state = State.ReadLiteralHeaderValueLengthPrefix;
                    break;

                case State.ReadLiteralHeaderNameLengthPrefix:
                    b = input.ReadSByte();
                    huffmanEncoded = (b & 0x80) == 0x80;
                    index = b & 0x7F;
                    if (index == 0x7f)
                    {
                        state = State.ReadLiteralHeaderNameLength;
                    }
                    else
                    {
                        nameLength = index;

                        if (nameLength == 0) throw new IOException("decompression failure");

                        if (ExceedsMaxHeaderSize(nameLength))
                        {
                            if (indexType == HpackUtil.IndexType.None)
                            {
                                name = ByteStream.Empty;
                                skipLength = nameLength;
                                state = State.SkipLiteralHeaderName;
                                break;
                            }

                            if (nameLength + HttpHeader.HttpHeaderOverhead > dynamicTable.Capacity)
                            {
                                dynamicTable.Clear();
                                name = Array.Empty<byte>();
                                skipLength = nameLength;
                                state = State.SkipLiteralHeaderName;
                                break;
                            }
                        }

                        state = State.ReadLiteralHeaderName;
                    }

                    break;

                case State.ReadLiteralHeaderNameLength:
                    nameLength = DecodeUle128(input);
                    if (nameLength == -1) return;

                    if (nameLength > int.MaxValue - index) throw new IOException("decompression failure");

                    nameLength += index;
                    if (ExceedsMaxHeaderSize(nameLength))
                    {
                        if (indexType == HpackUtil.IndexType.None)
                        {
                            name = ByteStream.Empty;
                            skipLength = nameLength;
                            state = State.SkipLiteralHeaderName;
                            break;
                        }
                        if (nameLength + HttpHeader.HttpHeaderOverhead > dynamicTable.Capacity)
                        {
                            dynamicTable.Clear();
                            name = ByteStream.Empty;
                            skipLength = nameLength;
                            state = State.SkipLiteralHeaderName;
                            break;
                        }
                    }

                    state = State.ReadLiteralHeaderName;
                    break;

                case State.ReadLiteralHeaderName:
                    if (input.BaseStream.Length - input.BaseStream.Position < nameLength) return;

                    name = ReadStringLiteral(input, nameLength);
                    state = State.ReadLiteralHeaderValueLengthPrefix;
                    break;

                case State.SkipLiteralHeaderName:

                    skipLength -= (int)input.BaseStream.Seek(skipLength, SeekOrigin.Current);
                    if (skipLength < 0) skipLength = 0;

                    if (skipLength == 0) state = State.ReadLiteralHeaderValueLengthPrefix;

                    break;

                case State.ReadLiteralHeaderValueLengthPrefix:
                    b = input.ReadSByte();
                    huffmanEncoded = (b & 0x80) == 0x80;
                    index = b & 0x7F;
                    if (index == 0x7f)
                    {
                        state = State.ReadLiteralHeaderValueLength;
                    }
                    else
                    {
                        valueLength = index;
                        var newHeaderSize1 = (long)nameLength + valueLength;
                        if (ExceedsMaxHeaderSize(newHeaderSize1))
                        {
                            headerSize = maxHeaderSize + 1;

                            if (indexType == HpackUtil.IndexType.None)
                            {
                                state = State.SkipLiteralHeaderValue;
                                break;
                            }
                            if (newHeaderSize1 + HttpHeader.HttpHeaderOverhead > dynamicTable.Capacity)
                            {
                                dynamicTable.Clear();
                                state = State.SkipLiteralHeaderValue;
                                break;
                            }
                        }

                        if (valueLength == 0)
                        {
                            name = Array.Empty<byte>();
                            state = State.ReadHeaderRepresentation;
                        }
                        else
                        {
                            state = State.ReadLiteralHeaderValue;
                        }
                    }

                    break;

                case State.ReadLiteralHeaderValueLength:
                    valueLength = DecodeUle128(input);
                    if (valueLength == -1) return;

                    if (valueLength > int.MaxValue - index) throw new IOException("decompression failure");

                    valueLength += index;

                    var newHeaderSize2 = (long)nameLength + valueLength;
                    if (newHeaderSize2 + headerSize > maxHeaderSize)
                    {
                        headerSize = maxHeaderSize + 1;

                        if (indexType == HpackUtil.IndexType.None)
                        {
                            state = State.SkipLiteralHeaderValue;
                            break;
                        }

                        if (newHeaderSize2 + HttpHeader.HttpHeaderOverhead > dynamicTable.Capacity)
                        {
                            dynamicTable.Clear();
                            state = State.SkipLiteralHeaderValue;
                            break;
                        }
                    }

                    state = State.ReadLiteralHeaderValue;
                    break;

                case State.ReadLiteralHeaderValue:
                    if (input.BaseStream.Length - input.BaseStream.Position < valueLength) return;

                    var value = ReadStringLiteral(input, valueLength);
                    InsertHeader(headerListener, name, value, indexType);
                    state = State.ReadHeaderRepresentation;
                    break;

                case State.SkipLiteralHeaderValue:
                    valueLength -= (int)input.BaseStream.Seek(valueLength, SeekOrigin.Current);
                    if (valueLength < 0) valueLength = 0;

                    if (valueLength == 0) state = State.ReadHeaderRepresentation;

                    break;

                default:
                    throw new Exception("should not reach here");
            }
    }

    public bool EndHeaderBlock()
    {
        var truncated = headerSize > maxHeaderSize;
        Reset();
        return truncated;
    }

    public void SetMaxHeaderTableSize(int maxHeaderTableSize)
    {
        maxDynamicTableSize = maxHeaderTableSize;
        if (maxDynamicTableSize < encoderMaxDynamicTableSize)
        {
            maxDynamicTableSizeChangeRequired = true;
            dynamicTable.SetCapacity(maxDynamicTableSize);
        }
    }
    public int GetMaxHeaderTableSize()
    {
        return dynamicTable.Capacity;
    }

    private void SetDynamicTableSize(int dynamicTableSize)
    {
        if (dynamicTableSize > maxDynamicTableSize) throw new IOException("invalid max dynamic table size");

        encoderMaxDynamicTableSize = dynamicTableSize;
        maxDynamicTableSizeChangeRequired = false;
        dynamicTable.SetCapacity(dynamicTableSize);
    }

    private HttpHeader GetHeaderField(int index)
    {
        if (index <= StaticTable.Length)
        {
            var headerField = StaticTable.Get(index);
            return headerField;
        }

        if (index - StaticTable.Length <= dynamicTable.Length())
        {
            var headerField = dynamicTable.GetEntry(index - StaticTable.Length);
            return headerField;
        }

        throw new IOException("illegal index value (" + index + ")");
    }

    private void ReadName(int index)
    {
        name = GetHeaderField(index).NameData;
    }

    private void IndexHeader(int index, IHeaderListener headerListener)
    {
        var headerField = GetHeaderField(index);
        AddHeader(headerListener, headerField.NameData, headerField.ValueData, false);
    }

    private void InsertHeader(IHeaderListener headerListener, ByteStream name, ByteStream value,
        HpackUtil.IndexType indexType)
    {
        AddHeader(headerListener, name, value, indexType == HpackUtil.IndexType.Never);

        switch (indexType)
        {
            case HpackUtil.IndexType.None:
            case HpackUtil.IndexType.Never:
                break;

            case HpackUtil.IndexType.Incremental:
                dynamicTable.Add(new HttpHeader(name, value));
                break;

            default:
                throw new Exception("should not reach here");
        }
    }

    private void AddHeader(IHeaderListener headerListener, ByteStream name, ByteStream value, bool sensitive)
    {
        if (name.Length == 0) throw new ArgumentException("name is empty");

        var newSize = headerSize + name.Length + value.Length;
        if (newSize <= maxHeaderSize)
        {
            headerListener.AddHeader(name, value, sensitive);
            headerSize = (int)newSize;
        }
        else
        {
            headerSize = maxHeaderSize + 1;
        }
    }

    private bool ExceedsMaxHeaderSize(long size)
    {
        if (size + headerSize <= maxHeaderSize) return false;

        headerSize = maxHeaderSize + 1;
        return true;
    }

    private ByteStream ReadStringLiteral(BinaryReader input, int length)
    {
        var buf = new byte[length];
        var lengthToRead = length;
        if (input.BaseStream.Length - input.BaseStream.Position < length)
            lengthToRead = (int)input.BaseStream.Length - (int)input.BaseStream.Position;

        var readBytes = input.Read(buf, 0, lengthToRead);
        if (readBytes != length) throw new IOException("decompression failure");

        return new ByteStream(huffmanEncoded ? HuffmanDecoder.Instance.Decode(buf) : buf);
    }
    private static int DecodeUle128(BinaryReader input)
    {
        var markedPosition = input.BaseStream.Position;
        var result = 0;
        var shift = 0;
        while (shift < 32)
        {
            if (input.BaseStream.Length - input.BaseStream.Position == 0)
            {
                input.BaseStream.Position = markedPosition;
                return -1;
            }

            var b = input.ReadSByte();
            if (shift == 28 && (b & 0xF8) != 0) break;

            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;

            shift += 7;
        }
        input.BaseStream.Position = markedPosition;
        throw new IOException("decompression failure");
    }

    private enum State
    {
        ReadHeaderRepresentation,
        ReadMaxDynamicTableSize,
        ReadIndexedHeader,
        ReadIndexedHeaderName,
        ReadLiteralHeaderNameLengthPrefix,
        ReadLiteralHeaderNameLength,
        ReadLiteralHeaderName,
        SkipLiteralHeaderName,
        ReadLiteralHeaderValueLengthPrefix,
        ReadLiteralHeaderValueLength,
        ReadLiteralHeaderValue,
        SkipLiteralHeaderValue
    }
}
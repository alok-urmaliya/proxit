#if NET6_0_OR_GREATER
using System;
using System.IO;
using T2Proxy.Models;

namespace T2Proxy.Http2.Hpack
{
    internal class Encoder
    {
        private const int BucketSize = 17;

        // a linked hash map of header fields
        private readonly HeaderEntry[] headerFields = new HeaderEntry[BucketSize];
        private readonly HeaderEntry head = new HeaderEntry(-1, ByteStream.Empty, ByteStream.Empty, int.MaxValue, null);
        private int size;

        public int MaxHeaderTableSize { get; private set; }

        public Encoder(int maxHeaderTableSize)
        {
            if (maxHeaderTableSize < 0)
            {
                throw new ArgumentException("Illegal Capacity: " + maxHeaderTableSize);
            }

            MaxHeaderTableSize = maxHeaderTableSize;
            head.Before = head.After = head;
        }

        public void EncodeHeader(BinaryWriter output, ByteStream name, ByteStream value, bool sensitive =
 false, HpackUtil.IndexType indexType = HpackUtil.IndexType.Incremental, bool useStaticName = true)
        {
            if (sensitive)
            {
                int nameIndex = GetNameIndex(name);
                EncodeLiteral(output, name, value, HpackUtil.IndexType.Never, nameIndex);
                return;
            }
            if (MaxHeaderTableSize == 0)
            {
                int staticTableIndex = StaticTable.GetIndex(name, value);
                if (staticTableIndex == -1)
                {
                    int nameIndex = StaticTable.GetIndex(name);
                    EncodeLiteral(output, name, value, HpackUtil.IndexType.None, nameIndex);
                }
                else
                {
                    EncodeInteger(output, 0x80, 7, staticTableIndex);
                }

                return;
            }

            int headerSize = HttpHeader.SizeOf(name, value);
            if (headerSize > MaxHeaderTableSize)
            {
                int nameIndex = GetNameIndex(name);
                EncodeLiteral(output, name, value, HpackUtil.IndexType.None, nameIndex);
                return;
            }

            var headerField = GetEntry(name, value);
            if (headerField != null)
            {
                int index = GetIndex(headerField.Index) + StaticTable.Length;

                EncodeInteger(output, 0x80, 7, index);
            }
            else
            {
                int staticTableIndex = StaticTable.GetIndex(name, value);
                if (staticTableIndex != -1)
                {
                    EncodeInteger(output, 0x80, 7, staticTableIndex);
                }
                else
                {
                    int nameIndex = useStaticName ? GetNameIndex(name) : -1;
                    EnsureCapacity(headerSize);

                    EncodeLiteral(output, name, value, indexType, nameIndex);
                    Add(name, value);
                }
            }
        }

        public void SetMaxHeaderTableSize(BinaryWriter output, int maxHeaderTableSize)
        {
            if (maxHeaderTableSize < 0)
            {
                throw new ArgumentException("Illegal Capacity", nameof(maxHeaderTableSize));
            }

            if (MaxHeaderTableSize == maxHeaderTableSize)
            {
                return;
            }

            MaxHeaderTableSize = maxHeaderTableSize;
            EnsureCapacity(0);
            EncodeInteger(output, 0x20, 5, maxHeaderTableSize);
        }

        private static void EncodeInteger(BinaryWriter output, int mask, int n, int i)
        {
            if (n < 0 || n > 8)
            {
                throw new ArgumentException("N: " + n);
            }

            int nbits = 0xFF >> (8 - n);
            if (i < nbits)
            {
                output.Write((byte)(mask | i));
            }
            else
            {
                output.Write((byte)(mask | nbits));
                int length = i - nbits;
                while (true)
                {
                    if ((length & ~0x7F) == 0)
                    {
                        output.Write((byte)length);
                        return;
                    }

                    output.Write((byte)((length & 0x7F) | 0x80));
                    length >>= 7;
                }
            }
        }
        private void EncodeStringLiteral(BinaryWriter output, ByteStream stringData)
        {
            int huffmanLength = HuffmanEncoder.Instance.GetEncodedLength(stringData);
            if (huffmanLength < stringData.Length)
            {
                EncodeInteger(output, 0x80, 7, huffmanLength);
                HuffmanEncoder.Instance.Encode(output, stringData);
            }
            else
            {
                EncodeInteger(output, 0x00, 7, stringData.Length);
                output.Write(stringData.Span);
            }
        }

        private void EncodeLiteral(BinaryWriter output, ByteStream name, ByteStream value, HpackUtil.IndexType indexType,
            int nameIndex)
        {
            int mask;
            int prefixBits;
            switch (indexType)
            {
                case HpackUtil.IndexType.Incremental:
                    mask = 0x40;
                    prefixBits = 6;
                    break;

                case HpackUtil.IndexType.None:
                    mask = 0x00;
                    prefixBits = 4;
                    break;

                case HpackUtil.IndexType.Never:
                    mask = 0x10;
                    prefixBits = 4;
                    break;

                default:
                    throw new Exception("should not reach here");
            }

            EncodeInteger(output, mask, prefixBits, nameIndex == -1 ? 0 : nameIndex);
            if (nameIndex == -1)
            {
                EncodeStringLiteral(output, name);
            }

            EncodeStringLiteral(output, value);
        }

        private int GetNameIndex(ByteStream name)
        {
            int index = StaticTable.GetIndex(name);
            if (index == -1)
            {
                index = GetIndex(name);
                if (index >= 0)
                {
                    index += StaticTable.Length;
                }
            }

            return index;
        }
        private void EnsureCapacity(int headerSize)
        {
            while (size + headerSize > MaxHeaderTableSize)
            {
                int index = Length();
                if (index == 0)
                {
                    break;
                }

                Remove();
            }
        }

        private int Length()
        {
            return size == 0 ? 0 : head.After.Index - head.Before.Index + 1;
        }

        private HeaderEntry? GetEntry(ByteStream name, ByteStream value)
        {
            if (Length() == 0 || name.Length == 0 || value.Length == 0)
            {
                return null;
            }

            int h = Hash(name);
            int i = Index(h);
            for (var e = headerFields[i]; e != null; e = e.Next)
            {
                if (e.Hash == h && name.Equals(e.NameData) && Equals(value, e.ValueData))
                {
                    return e;
                }
            }

            return null;
        }

        private int GetIndex(ByteStream name)
        {
            if (Length() == 0 || name.Length == 0)
            {
                return -1;
            }

            int h = Hash(name);
            int i = Encoder.Index(h);
            int index = -1;
            for (HeaderEntry? e = headerFields[i]; e != null; e = e.Next)
            {
                if (e.Hash == h && name.Equals(e.NameData))
                {
                    index = e.Index;
                    break;
                }
            }

            return GetIndex(index);
        }
        private int GetIndex(int index)
        {
            if (index == -1)
            {
                return index;
            }

            return index - head.Before.Index + 1;
        }

        private void Add(ByteStream name, ByteStream value)
        {
            int headerSize = HttpHeader.SizeOf(name, value);

            if (headerSize > MaxHeaderTableSize)
            {
                Clear();
                return;
            }

            while (size + headerSize > MaxHeaderTableSize)
            {
                Remove();
            }

            int h = Hash(name);
            int i = Index(h);
            var old = headerFields[i];
            var e = new HeaderEntry(h, name, value, head.Before.Index - 1, old);
            headerFields[i] = e;
            e.AddBefore(head);
            size += headerSize;
        }

        private HttpHeader? Remove()
        {
            if (size == 0)
            {
                return null;
            }

            var eldest = head.After;
            int h = eldest.Hash;
            int i = Index(h);
            var prev = headerFields[i];
            var e = prev;
            while (e != null)
            {
                var next = e.Next;
                if (e == eldest)
                {
                    if (prev == eldest)
                    {
                        headerFields[i] = next;
                    }
                    else
                    {
                        prev!.Next = next;
                    }

                    eldest.Remove();
                    size -= eldest.Size;
                    return eldest;
                }

                prev = e;
                e = next;
            }

            return null;
        }

        private void Clear()
        {
            for (int i = 0; i < headerFields.Length; i++)
            {
                headerFields[i] = null;
            }

            head.Before = head.After = head;
            size = 0;
        }

        private static int Hash(ByteStream name)
        {
            int h = 0;
            for (int i = 0; i < name.Length; i++)
            {
                h = 31 * h + name.Span[i];
            }

            if (h > 0)
            {
                return h;
            }

            if (h == int.MinValue)
            {
                return int.MaxValue;
            }

            return -h;
        }

        private static int Index(int h)
        {
            return h % BucketSize;
        }

        private class HeaderEntry : HttpHeader
        {
            public HeaderEntry Before { get; set; }

            public HeaderEntry After { get; set; }

            public HeaderEntry? Next { get; set; }

            public int Hash { get; }

            public int Index { get; }

            public HeaderEntry(int hash, ByteStream name, ByteStream value, int index, HeaderEntry? next) : base(name, value, true)
            {
                Index = index;
                Hash = hash;
                Next = next;
                Before = this;
                After = this;
            }

            public void Remove()
            {
                Before.After = After;
                After.Before = Before;
            }

            public void AddBefore(HeaderEntry existingEntry)
            {
                After = existingEntry;
                Before = existingEntry.Before;
                Before.After = this;
                After.Before = this;
            }
        }
    }
}
#endif
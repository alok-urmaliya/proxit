#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace T2Proxy.EventArguments
{

    public class BeforeBodyWriteEventArgs : ServerEventArgsBase
    {
        internal BeforeBodyWriteEventArgs(SessionEventArgs session, byte[] bodyBytes, bool isChunked, bool isLastChunk) : base(session.Server, session.ClientConnection)
        {
            Session = session;
            BodyBytes = bodyBytes;
            IsChunked = isChunked;
            IsLastChunk = isLastChunk;
        }

        public SessionEventArgs Session { get; }

        public bool IsChunked { get; }

        public bool IsLastChunk { get; set; }

        public byte[] BodyBytes { get; set; }
    }
}
#endif
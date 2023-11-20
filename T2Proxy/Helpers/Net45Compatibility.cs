#if NET451
using System.Threading.Tasks;

namespace T2Proxy
{
    internal class Net45Compatibility
    {
        public static byte[] EmptyArray = new byte[0];

        public static Task CompletedTask = Task.FromResult<object>(null);
    }
}
#endif
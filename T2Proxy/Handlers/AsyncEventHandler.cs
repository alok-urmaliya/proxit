using System.Threading.Tasks;

namespace T2Proxy.EventArguments;

public delegate Task AsyncEventHandler<in TEventArgs>(object sender, TEventArgs e);
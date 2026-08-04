using System.Windows;

namespace DotStream.Simulator;

public sealed class DeckDropEventArgs : EventArgs
{
    public DeckDropEventArgs(int protocolIndex, IDataObject data)
    {
        ProtocolIndex = protocolIndex;
        Data = data;
    }

    public int ProtocolIndex { get; }

    public IDataObject Data { get; }
}

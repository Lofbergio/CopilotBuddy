using System;

namespace GreenMagic
{
    /// <summary>
    /// Interface for memory operations (patches, detours, etc.).
    /// </summary>
    public interface IMemoryOperation : IDisposable
    {
        bool IsApplied { get; }
        string Name { get; }
        bool Remove();
        bool Apply();
    }
}

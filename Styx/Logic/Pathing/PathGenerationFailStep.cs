using System;

namespace Styx.Logic.Pathing
{
    public enum PathGenerationFailStep
    {
        None = -1,
        Success,
        FindStartNode,
        FindEndNode,
        FindPath,
        Mesh
    }
}

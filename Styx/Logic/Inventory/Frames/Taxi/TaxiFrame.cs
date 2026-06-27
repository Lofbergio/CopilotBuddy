// TaxiFrame.cs - Taxi map UI frame
// Ported from HB 4.3.4 - Styx\Logic\Inventory\Frames\Taxi\TaxiFrame.cs

using Styx.WoWInternals;
using System;
using System.Collections.Generic;

namespace Styx.Logic.Inventory.Frames.Taxi
{
    public class TaxiFrame : Frame
    {
        public static readonly TaxiFrame Instance = new TaxiFrame();

        public override void Hide() => Lua.DoString("CloseTaxiMap()");

        public TaxiFrame() : base(nameof(TaxiFrame))
        {
        }

        public uint NumNodes => Lua.GetReturnVal<uint>("return NumTaxiNodes()", 0U);

        public List<TaxiFrameNode> Nodes
        {
            get
            {
                List<TaxiFrameNode> taxiFrameNodeList = new List<TaxiFrameNode>();
                List<TaxiFrameNode> nodes;
                using (new FrameLock())
                {
                    uint numNodes = this.NumNodes;
                    for (uint slot = 1; slot <= numNodes; ++slot)
                        taxiFrameNodeList.Add(new TaxiFrameNode(slot));
                    nodes = taxiFrameNodeList;
                }
                return nodes;
            }
        }

        public enum NodeType
        {
            Current,
            Reachable,
            Distant,   // known node not directly reachable from here — TaxiNodeGetType can return "DISTANT"
            None,
        }

        public class TaxiFrameNode
        {
            private string _name;
            private NodeType? _type;

            public string Name
            {
                get
                {
                    if (string.IsNullOrEmpty(_name))
                        _name = Lua.GetReturnVal<string>($"return TaxiNodeName({Slot})", 0U);
                    return _name;
                }
            }

            public uint Slot { get; set; }

            internal TaxiFrameNode(uint slot) => Slot = slot;

            public void TakeNode() => Lua.DoString($"TakeTaxiNode({Slot})");

            public NodeType Type
            {
                get
                {
                    if (!_type.HasValue)
                    {
                        // Safe-parse: WotLK TaxiNodeGetType returns CURRENT/REACHABLE/DISTANT/NONE — an
                        // unknown value must not throw (would kill the whole TAXIMAP_OPENED handler).
                        string raw = Lua.GetReturnVal<string>($"return TaxiNodeGetType({Slot})", 0U);
                        _type = Enum.TryParse(raw, true, out NodeType parsed) ? parsed : NodeType.None;
                    }
                    return _type.Value;
                }
            }

            public bool IsCurrent => Type == NodeType.Current;

            public bool Reachable => Type == NodeType.Reachable || IsCurrent;

            public override string ToString() => $"Slot: {Slot}, Name: {Name}, Type: {Type}";
        }
    }
}

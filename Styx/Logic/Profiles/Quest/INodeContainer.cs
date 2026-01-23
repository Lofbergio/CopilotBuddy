// Decompiled with JetBrains decompiler
// Type: Styx.Logic.Profiles.Quest.INodeContainer
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe

using System.Collections.Generic;

#nullable disable
namespace Styx.Logic.Profiles.Quest;

public interface INodeContainer
{
    IEnumerable<OrderNode> GetNodes();
}

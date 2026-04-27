using System;
using System.Drawing;
using Bots.DungeonBuddy.Helpers;
using TreeSharp;

namespace Bots.DungeonBuddy.Actions
{
	// Token: 0x02000131 RID: 305
	public class ActionLogger : global::TreeSharp.Action
	{
		// Token: 0x06000B4F RID: 2895 RVA: 0x0000796D File Offset: 0x00005B6D
		public ActionLogger(string format, params object[] args)
		{
			this.string_0 = format;
			this.object_0 = args;
		}

		// Token: 0x06000B50 RID: 2896 RVA: 0x00007985 File Offset: 0x00005B85
		public ActionLogger(Color color, string format, params object[] args)
			: this(format, args)
		{
			this.color_0 = color;
			this.bool_0 = true;
		}

		// Token: 0x06000B51 RID: 2897 RVA: 0x00045AB8 File Offset: 0x00043CB8
		protected override RunStatus Run(object context)
		{
			if (this.bool_0)
			{
				Styx.Helpers.Logging.Write(this.color_0, this.string_0, this.object_0);
			}
			else
			{
				Styx.Helpers.Logging.Write(this.string_0, this.object_0);
			}
			RunStatus runStatus;
			if (base.Parent != null && base.Parent is Selector)
			{
				runStatus = RunStatus.Failure;
			}
			else
			{
				runStatus = RunStatus.Success;
			}
			return runStatus;
		}

		// Token: 0x0400069F RID: 1695
		private readonly Color color_0;

		// Token: 0x040006A0 RID: 1696
		private readonly string string_0;

		// Token: 0x040006A1 RID: 1697
		private readonly object[] object_0;

		// Token: 0x040006A2 RID: 1698
		private readonly bool bool_0;
	}
}

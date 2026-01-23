using System;
using System.Collections.Generic;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;

namespace Styx.Logic.AreaManagement
{
	public class AreaManager
	{
		private readonly List<Area> _areas = new List<Area>();
		private GrindArea? _currentGrindArea;

		public void SetAreaByIndex(int index)
		{
			if (index > _areas.Count)
			{
				throw new IndexOutOfRangeException("Index out of range for area list.");
			}
			// TODO: Implement polygon area handling
		}

		public void SetArea(GrindArea area)
		{
			_currentGrindArea = area;
		}

		public void Add(Area value)
		{
			_areas.Add(value);
		}

		public GrindArea? CurrentGrindArea
		{
			get
			{
				GrindArea? area = _currentGrindArea;
				if (area == null)
				{
					if (ProfileManager.CurrentProfile != null)
					{
						return ProfileManager.CurrentProfile.GrindArea;
					}
					return null;
				}
				return area;
			}
		}

		public AreaManager()
		{
		}
	}
}

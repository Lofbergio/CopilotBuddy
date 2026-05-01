using System.Xml.Linq;

namespace Styx.Logic.Profiles
{
    public interface IXmlObject
    {
        XElement Element { get; }
    }
}

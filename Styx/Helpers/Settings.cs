using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace Styx.Helpers
{
	public abstract class Settings
	{
		private readonly string _settingsPath;

		protected Settings(string settingsPath)
		{
			_settingsPath = settingsPath;
			InitializeDefaultValues();
			if (!File.Exists(_settingsPath))
			{
				SaveToFile(_settingsPath);
			}
			else
			{
				LoadFromXML(XElement.Load(_settingsPath));
			}
		}

		public Dictionary<string, object> GetSettings()
		{
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			Type type = this.GetType();
			foreach (PropertyInfo propertyInfo in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.GetProperty | BindingFlags.SetProperty))
			{
				object[] attributes = propertyInfo.GetCustomAttributes(typeof(SettingAttribute), false);
				if (attributes != null && attributes.Length > 0)
				{
					object value = propertyInfo.GetValue(this, null);
					string elementName = ((SettingAttribute)attributes[0]).ElementName ?? propertyInfo.Name;
					dictionary.Add(elementName, value);
				}
			}
			return dictionary;
		}

		public void InitializeDefaultValues()
		{
			Type type = this.GetType();
			foreach (PropertyInfo propertyInfo in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.GetProperty | BindingFlags.SetProperty))
			{
				object[] attributes = propertyInfo.GetCustomAttributes(typeof(DefaultValueAttribute), false);
				if (attributes != null && attributes.Length > 0)
				{
					DefaultValueAttribute defaultValueAttribute = (DefaultValueAttribute)attributes[0];
					try
					{
						propertyInfo.SetValue(this, defaultValueAttribute.Value, null);
					}
					catch (TargetException)
					{
						Logging.WriteDebug("Could not set property {0} to {1} (DefaultValueAttribute)",
							propertyInfo.Name,
							defaultValueAttribute.Value == null ? "null" : defaultValueAttribute.Value.ToString());
					}
				}
			}
		}

		public void SaveToFile(string path)
		{
			try
			{
				string directory = Path.GetDirectoryName(path);
				if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
				{
					Directory.CreateDirectory(directory);
				}
				GetXML().Save(path);
			}
			catch (IOException ex)
			{
				Logging.Write("Error trying to save the settings file: " + ex.Message);
			}
		}

		public XElement GetXML()
		{
			Type type = this.GetType();
			PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.GetProperty | BindingFlags.SetProperty);
			XElement root = new XElement(type.Name);

			foreach (PropertyInfo propertyInfo in properties)
			{
				object value = propertyInfo.GetValue(this, null);
				if (value != null)
				{
					object[] attributes = propertyInfo.GetCustomAttributes(typeof(SettingAttribute), false);
					if (attributes != null && attributes.Length > 0)
					{
						SettingAttribute settingAttribute = (SettingAttribute)attributes[0];
						string elementName = settingAttribute.ElementName ?? propertyInfo.Name;

						if (!string.IsNullOrEmpty(settingAttribute.Explanation))
						{
							root.Add(new XComment(settingAttribute.Explanation));
						}

						XElement element;
						if (propertyInfo.PropertyType.IsArray)
						{
							element = new XElement(elementName);
							Array array = (Array)value;
							for (int i = 0; i < array.Length; i++)
							{
								element.Add(new XElement("Entry", array.GetValue(i).ToString()));
							}
						}
						else
						{
							element = new XElement(elementName, value.ToString());
						}
						root.Add(element);
					}
				}
			}
			return root;
		}

		public void LoadFromXML(XElement root)
		{
			if (root == null)
			{
				throw new ArgumentNullException("root");
			}

			Type type = this.GetType();
			if (root.Name != type.Name)
			{
				throw new ArgumentException(
					string.Format("The settings file does not match the type that is being loaded! Root name is \"{0}\", expected \"{1}\".",
						root.Name, type.Name),
					"root");
			}

			foreach (PropertyInfo propertyInfo in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.GetProperty | BindingFlags.SetProperty))
			{
				object[] attributes = propertyInfo.GetCustomAttributes(typeof(SettingAttribute), false);
				if (attributes != null && attributes.Length > 0)
				{
					SettingAttribute settingAttribute = (SettingAttribute)attributes[0];
					string elementName = settingAttribute.ElementName ?? propertyInfo.Name;
					XElement element = root.Element(elementName);

					if (element != null)
					{
						string textValue = element.Value;
						object convertedValue;

						if (propertyInfo.PropertyType.IsArray)
						{
							Type elementType = propertyInfo.PropertyType.GetElementType();
							XElement[] entries = element.Elements("Entry").ToArray();
							List<object> list = new List<object>();

							foreach (XElement entry in entries)
							{
								object obj = ConvertValue(entry.Value, elementType, "Entry in " + element.Name);
								if (obj != null)
								{
									list.Add(obj);
								}
							}

							Array array = Array.CreateInstance(elementType, list.Count);
							for (int i = 0; i < list.Count; i++)
							{
								array.SetValue(list[i], i);
							}
							convertedValue = array;
						}
						else
						{
							convertedValue = ConvertValue(textValue, propertyInfo.PropertyType, element.Name.ToString());
						}

						propertyInfo.SetValue(this, convertedValue, null);
					}
				}
			}
		}

		public void Save()
		{
			SaveToFile(_settingsPath);
		}

		public void Load()
		{
			LoadFromXML(XElement.Load(_settingsPath));
		}

		public static T LoadFromXML<T>(XElement root) where T : Settings, new()
		{
			T instance = new T();
			instance.LoadFromXML(root);
			return instance;
		}

		public static T LoadFromFile<T>(string path) where T : Settings, new()
		{
			if (!File.Exists(path))
			{
				throw new FileNotFoundException("Could not find the settings path!", path);
			}
			return LoadFromXML<T>(XElement.Load(path));
		}

		private static object ConvertValue(string val, Type t, string elementName)
		{
			try
			{
				if (t.IsEnum)
				{
					return Enum.Parse(t, val);
				}
				return Convert.ChangeType(val, t);
			}
			catch (InvalidCastException)
			{
				Logging.Write("Setting {0} has invalid value \"{1}\"!", elementName, val);
			}
			catch (FormatException)
			{
				Logging.Write("Setting {0} has invalid format \"{1}\"!", elementName, val);
			}
			return null;
		}
	}
}

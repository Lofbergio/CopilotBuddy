using System;
using System.ComponentModel;
using System.Drawing.Design;
using System.Windows.Forms;
using System.Windows.Forms.Design;

namespace Styx.Helpers
{
    public class FlagEnumUIEditor : UITypeEditor
    {
        private FlagCheckedListBox _checkedListBox;

        public FlagEnumUIEditor()
        {
            this._checkedListBox = new FlagCheckedListBox();
            this._checkedListBox.BorderStyle = BorderStyle.None;
        }

        public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
        {
            if (context != null && context.Instance != null && provider != null)
            {
                IWindowsFormsEditorService windowsFormsEditorService = (IWindowsFormsEditorService)provider.GetService(typeof(IWindowsFormsEditorService));
                if (windowsFormsEditorService != null)
                {
                    Enum @enum = (Enum)Convert.ChangeType(value, context.PropertyDescriptor.PropertyType);
                    this._checkedListBox.EnumValue = @enum;
                    windowsFormsEditorService.DropDownControl(this._checkedListBox);
                    return this._checkedListBox.EnumValue;
                }
            }
            return null;
        }

        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context)
        {
            return UITypeEditorEditStyle.DropDown;
        }
    }
}

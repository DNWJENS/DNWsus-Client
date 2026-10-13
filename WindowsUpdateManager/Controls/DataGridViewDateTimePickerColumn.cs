using System;
using System.Windows.Forms;
using WindowsUpdateManager.Theme;

namespace WindowsUpdateManager.Controls
{
    // -----------------------------------------------------------------------
    // Column
    // -----------------------------------------------------------------------
    public class DataGridViewDateTimePickerColumn : DataGridViewColumn
    {
        public DataGridViewDateTimePickerColumn()
            : base(new DataGridViewDateTimePickerCell())
        {
        }

        public override DataGridViewCell CellTemplate
        {
            get { return base.CellTemplate; }
            set
            {
                if (value != null && !(value is DataGridViewDateTimePickerCell))
                    throw new InvalidCastException("CellTemplate must be a DataGridViewDateTimePickerCell");
                base.CellTemplate = value;
            }
        }
    }

    // -----------------------------------------------------------------------
    // Cell
    // -----------------------------------------------------------------------
    public class DataGridViewDateTimePickerCell : DataGridViewTextBoxCell
    {
        public DataGridViewDateTimePickerCell()
        {
            ValueType = typeof(DateTime?);
        }

        public override Type EditType
        {
            get { return typeof(DateTimePickerEditingControl); }
        }

        public override Type ValueType
        {
            get { return typeof(DateTime?); }
        }

        public override object DefaultNewRowValue
        {
            get { return null; }
        }

        protected override object GetFormattedValue(
            object value,
            int rowIndex,
            ref DataGridViewCellStyle cellStyle,
            System.ComponentModel.TypeConverter valueTypeConverter,
            System.ComponentModel.TypeConverter formattedValueTypeConverter,
            DataGridViewDataErrorContexts context)
        {
            if (value == null || value == DBNull.Value)
                return "--";

            if (value is DateTime)
                return ((DateTime)value).ToString("dd.MM.yyyy HH:mm");

            return "--";
        }

        public override void InitializeEditingControl(
            int rowIndex,
            object initialFormattedValue,
            DataGridViewCellStyle dataGridViewCellStyle)
        {
            base.InitializeEditingControl(rowIndex, initialFormattedValue, dataGridViewCellStyle);

            var ctl = DataGridView.EditingControl as DateTimePickerEditingControl;
            if (ctl == null) return;

            if (Value != null && Value != DBNull.Value && Value is DateTime)
            {
                ctl.Value = (DateTime)Value;
            }
            else
            {
                ctl.Value = DateTime.Now.AddHours(1);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Variant column that shows "Sofort" instead of "--" for null values
    // -----------------------------------------------------------------------
    public class DataGridViewInstallTimePickerColumn : DataGridViewColumn
    {
        public DataGridViewInstallTimePickerColumn()
            : base(new DataGridViewInstallTimePickerCell())
        {
        }

        public override DataGridViewCell CellTemplate
        {
            get { return base.CellTemplate; }
            set
            {
                if (value != null && !(value is DataGridViewInstallTimePickerCell))
                    throw new InvalidCastException("CellTemplate must be a DataGridViewInstallTimePickerCell");
                base.CellTemplate = value;
            }
        }
    }

    public class DataGridViewInstallTimePickerCell : DataGridViewDateTimePickerCell
    {
        protected override object GetFormattedValue(
            object value,
            int rowIndex,
            ref DataGridViewCellStyle cellStyle,
            System.ComponentModel.TypeConverter valueTypeConverter,
            System.ComponentModel.TypeConverter formattedValueTypeConverter,
            DataGridViewDataErrorContexts context)
        {
            if (value == null || value == DBNull.Value)
                return "Sofort";

            if (value is DateTime)
                return ((DateTime)value).ToString("dd.MM.yyyy HH:mm");

            return "Sofort";
        }
    }

    // -----------------------------------------------------------------------
    // Editing Control
    // -----------------------------------------------------------------------
    public class DateTimePickerEditingControl : DateTimePicker, IDataGridViewEditingControl
    {
        private DataGridView _dataGridView;
        private int _rowIndex;
        private bool _valueChanged;

        public DateTimePickerEditingControl()
        {
            Format       = DateTimePickerFormat.Custom;
            CustomFormat = "dd.MM.yyyy HH:mm";
            ShowUpDown   = false;

            // Dark colors
            BackColor                = DarkTheme.Surface;
            ForeColor                = DarkTheme.Text;
            CalendarMonthBackground  = DarkTheme.Background;
            CalendarForeColor        = DarkTheme.Text;
            CalendarTitleBackColor   = DarkTheme.Surface;
            CalendarTitleForeColor   = DarkTheme.Text;
            CalendarTrailingForeColor = DarkTheme.TextMuted;
        }

        // ---- IDataGridViewEditingControl ----

        public DataGridView EditingControlDataGridView
        {
            get { return _dataGridView; }
            set { _dataGridView = value; }
        }

        public object EditingControlFormattedValue
        {
            get { return Value.ToString("dd.MM.yyyy HH:mm"); }
            set
            {
                if (value is DateTime)
                {
                    Value = (DateTime)value;
                }
                else if (value is string)
                {
                    DateTime parsed;
                    if (DateTime.TryParse((string)value, out parsed))
                        Value = parsed;
                }
            }
        }

        public int EditingControlRowIndex
        {
            get { return _rowIndex; }
            set { _rowIndex = value; }
        }

        public bool EditingControlValueChanged
        {
            get { return _valueChanged; }
            set { _valueChanged = value; }
        }

        public Cursor EditingPanelCursor
        {
            get { return Cursors.Default; }
        }

        public bool RepositionEditingControlOnValueChange
        {
            get { return false; }
        }

        public void ApplyCellStyleToEditingControl(DataGridViewCellStyle dataGridViewCellStyle)
        {
            if (dataGridViewCellStyle.Font != null)
                Font = dataGridViewCellStyle.Font;
            BackColor = DarkTheme.Surface;
            ForeColor = DarkTheme.Text;
        }

        public bool EditingControlWantsInputKey(Keys keyData, bool dataGridViewWantsInputKey)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                case Keys.Home:
                case Keys.End:
                case Keys.PageDown:
                case Keys.PageUp:
                    return true;
                default:
                    return !dataGridViewWantsInputKey;
            }
        }

        public object GetEditingControlFormattedValue(DataGridViewDataErrorContexts context)
        {
            return Value.ToString("dd.MM.yyyy HH:mm");
        }

        public void PrepareEditingControlForEdit(bool selectAll)
        {
            // Nothing needed
        }

        protected override void OnValueChanged(EventArgs e)
        {
            _valueChanged = true;
            if (_dataGridView != null)
                _dataGridView.NotifyCurrentCellDirty(true);
            base.OnValueChanged(e);
        }
    }
}

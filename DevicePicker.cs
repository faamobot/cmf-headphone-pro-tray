namespace CmfTray;

/// <summary>Tiny modal list for picking which paired device to connect to.</summary>
public sealed class DevicePicker : Form
{
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly IReadOnlyList<RfcommClient.Candidate> _items;

    public RfcommClient.Candidate? Selected { get; private set; }

    public DevicePicker(IReadOnlyList<RfcommClient.Candidate> items)
    {
        _items = items;
        Text = "Choose CMF Headphone Pro";
        Width = 360;
        Height = 300;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        foreach (var c in items)
            _list.Items.Add($"{c.Name}  ({c.Address})");
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        _list.DoubleClick += (_, _) => Accept();

        var ok = new Button { Text = "Connect", DialogResult = DialogResult.OK, Dock = DockStyle.Right, Width = 90 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Dock = DockStyle.Right, Width = 90 };
        ok.Click += (_, _) => Accept();

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(6) };
        bottom.Controls.Add(ok);
        bottom.Controls.Add(cancel);

        Controls.Add(_list);
        Controls.Add(bottom);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void Accept()
    {
        int i = _list.SelectedIndex;
        if (i >= 0 && i < _items.Count) Selected = _items[i];
        DialogResult = DialogResult.OK;
        Close();
    }
}

using System.Text.RegularExpressions;

namespace HostsFileEditor;

internal class RawEditForm : Form
{
    private readonly RichTextBox _rtb;
    private readonly Label _statusLabel;
    private readonly Panel _findPanel;
    private readonly TextBox _findBox;
    private readonly TextBox _replaceBox;
    private readonly CheckBox _wholeWordCheck;
    private bool _isHighlighting;

    // Aligns entries into two columns so hostnames start at the same column.
    // The "# " prefix of disabled entries is included in the width calculation.
    public static string FormatAligned(IEnumerable<HostsEntry> entries)
    {
        var list = entries.ToList();

        // Width of the left column: for disabled entries, "# " counts toward the width
        var maxColWidth = list
            .Where(e => e.Valid && !string.IsNullOrEmpty(e.IpAddress))
            .Select(e => (e.Enabled ? 0 : 2) + e.IpAddress.Length)
            .DefaultIfEmpty(0)
            .Max();

        return string.Join(Environment.NewLine, list.Select(e =>
        {
            if (e.HasCommentOnly || !e.Valid || string.IsNullOrEmpty(e.IpAddress))
                return e.UnparsedText;

            var comment = e.Comment.Trim().Length > 0 ? "  # " + e.Comment.Trim() : "";
            if (e.Enabled)
                return $"{e.IpAddress.PadRight(maxColWidth)}  {e.HostNames}{comment}";
            else
                return $"# {e.IpAddress.PadRight(maxColWidth - 2)}  {e.HostNames}{comment}";
        }));
    }

    public RawEditForm(string rawText)
    {
        Text = "Raw Edit — hosts file";
        Size = new Size(820, 600);
        MinimumSize = new Size(600, 400);
        StartPosition = FormStartPosition.CenterParent;

        _rtb = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 10f),
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both,
            AcceptsTab = true,
            DetectUrls = false,
            Text = rawText
        };
        _rtb.TextChanged += OnTextChanged;

        var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 46 };
        _statusLabel = new Label
        {
            AutoSize = true,
            Location = new Point(12, 14),
            ForeColor = SystemColors.GrayText
        };
        var cancelBtn = new Button
        {
            Text = "Cancel",
            Size = new Size(90, 28),
            DialogResult = DialogResult.Cancel
        };
        var applyBtn = new Button
        {
            Text = "Apply",
            Size = new Size(90, 28),
            DialogResult = DialogResult.OK
        };
        cancelBtn.Location = new Point(btnPanel.Width - 200, 9);
        applyBtn.Location = new Point(btnPanel.Width - 102, 9);
        cancelBtn.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        applyBtn.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        btnPanel.Controls.AddRange([_statusLabel, cancelBtn, applyBtn]);

        var sep = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = SystemColors.ControlDark };

        // ── Find & Replace bar — always visible ─────────────────────────────
        // "Match whole word" defaults on so replacing an IP like "10.0.0.5" can't
        // also clobber "10.0.0.50" on another line — a real risk when an IP repeats
        // across several hostnames and you want to repoint all of them at once.
        _findPanel = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 4, 8, 4) };

        var findLabel = new Label { Text = "Find:", AutoSize = true, Location = new Point(8, 9) };
        _findBox = new TextBox { Location = new Point(46, 6), Width = 160 };

        var replaceLabel = new Label { Text = "Replace:", AutoSize = true, Location = new Point(214, 9) };
        _replaceBox = new TextBox { Location = new Point(268, 6), Width = 160 };

        _wholeWordCheck = new CheckBox { Text = "Whole word", AutoSize = true, Checked = true, Location = new Point(436, 8) };

        var replaceAllBtn = new Button { Text = "Replace All", AutoSize = true, Location = new Point(548, 4) };
        replaceAllBtn.Click += (_, _) => ReplaceAll();

        _findPanel.Controls.AddRange([findLabel, _findBox, replaceLabel, _replaceBox, _wholeWordCheck, replaceAllBtn]);

        foreach (Control c in new Control[] { _findBox, _replaceBox })
        {
            c.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter) { ReplaceAll(); e.SuppressKeyPress = true; }
            };
        }

        AcceptButton = applyBtn;
        CancelButton = cancelBtn;

        Controls.AddRange([_rtb, sep, btnPanel, _findPanel]);

        KeyPreview = true;
        KeyDown += OnFormKeyDown;

        Shown += (_, _) => { ApplySyntaxHighlight(); UpdateStatus(); };
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.H)
        {
            // Pull whatever's selected in the editor (e.g. an IP) into both fields,
            // so the common case — repoint this IP everywhere — only needs editing
            // the Replace box before hitting Enter/Replace All.
            var selected = _rtb.SelectedText.Trim();
            if (!string.IsNullOrEmpty(selected))
            {
                _findBox.Text = selected;
                _replaceBox.Text = selected;
            }

            _replaceBox.Focus();
            _replaceBox.SelectAll();
            e.SuppressKeyPress = true;
        }
    }

    private void ReplaceAll()
    {
        var find = _findBox.Text;
        if (string.IsNullOrEmpty(find))
        {
            _statusLabel.Text = "Enter text to find.";
            return;
        }

        var pattern = Regex.Escape(find);
        if (_wholeWordCheck.Checked)
            pattern = $@"\b{pattern}\b";

        var replacement = _replaceBox.Text;
        var count = 0;

        var newText = Regex.Replace(_rtb.Text, pattern, _ =>
        {
            count++;
            return replacement;
        });

        if (count == 0)
        {
            _statusLabel.Text = $"No matches for \"{find}\".";
            return;
        }

        var selStart = _rtb.SelectionStart;
        _rtb.Text = newText;
        _rtb.SelectionStart = Math.Min(selStart, _rtb.TextLength);

        UpdateStatus();
        _statusLabel.Text = $"Replaced {count} occurrence{(count == 1 ? "" : "s")}.  ·  {_statusLabel.Text}";
    }

    public string[] GetLines() => _rtb.Lines;

    private void OnTextChanged(object? sender, EventArgs e)
    {
        if (_isHighlighting) return;
        ApplySyntaxHighlight();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var lines = _rtb.Lines;
        var commentCount = lines.Count(l => l.TrimStart().StartsWith('#'));
        var entryCount = lines.Count(l =>
        {
            var t = l.TrimStart();
            return t.Length > 0 && !t.StartsWith('#');
        });
        _statusLabel.Text = $"{lines.Length} lines  ·  {entryCount} entries  ·  {commentCount} comments";
    }

    private void ApplySyntaxHighlight()
    {
        _isHighlighting = true;
        var selStart = _rtb.SelectionStart;
        var selLen = _rtb.SelectionLength;
        try
        {
            int pos = 0;
            foreach (var line in _rtb.Lines)
            {
                ColorLine(pos, line);
                pos += line.Length + 1;
            }
        }
        finally
        {
            _rtb.Select(selStart, selLen);
            _isHighlighting = false;
        }
    }

    private void ColorLine(int pos, string line)
    {
        if (line.Length == 0) return;

        var trimmed = line.TrimStart();

        if (trimmed.Length == 0 || trimmed[0] == '#')
        {
            _rtb.Select(pos, line.Length);
            _rtb.SelectionColor = Color.Gray;
            return;
        }

        var leadingSpaces = line.Length - trimmed.Length;
        var split = trimmed.IndexOfAny([' ', '\t']);

        if (split <= 0)
        {
            _rtb.Select(pos, line.Length);
            _rtb.SelectionColor = Color.FromArgb(0, 100, 200);
            return;
        }

        // IP portion — blue
        _rtb.Select(pos + leadingSpaces, split);
        _rtb.SelectionColor = Color.FromArgb(0, 100, 200);

        var afterIp = trimmed.Substring(split);
        var afterIpStart = pos + leadingSpaces + split;

        var commentIdx = afterIp.IndexOf('#');
        if (commentIdx >= 0)
        {
            // Hostname — black
            _rtb.Select(afterIpStart, commentIdx);
            _rtb.SelectionColor = Color.Black;
            // Inline comment — gray
            _rtb.Select(afterIpStart + commentIdx, afterIp.Length - commentIdx);
            _rtb.SelectionColor = Color.Gray;
        }
        else
        {
            _rtb.Select(afterIpStart, afterIp.Length);
            _rtb.SelectionColor = Color.Black;
        }
    }
}

using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SeppWinFormsApp.SeppWinFormsApp;

namespace SeppWinFormsApp
{
    public partial class Galgje : Form
    {
        private MenuStrip _menuStrip;
        private ToolStripMenuItem _menuSpel;
        private ToolStripMenuItem _menuInstellingen;

        // Stel defaults in (kunnen later overschreven worden via dialoog)
        private int _minWordLength = 5;
        private int _maxWordLength = 10;

        private Dictionary<string, Button> _keyboardButtons = new();

        // --- Nieuw: spelstatus ---
        private readonly Random _rng = new();

        private List<string> _wordList = new();

        private string _targetWord = string.Empty;    // het gekozen woord
        private char[] _revealed = Array.Empty<char>(); // wat zichtbaar is (_ of letter)
        private HashSet<char> _guessed = new();       // alle geraden letters (A-Z)
        private Label _wordBar = null!;               // label boven het keyboard
        private PictureBox _hangmanBox;
        private int _wrongGuesses = 0;

        private void CreateHangmanBox()
        {
            _hangmanBox = new PictureBox
            {
                Left = 770,
                Top = 140,
                Width = 200,
                Height = 200,
                BackColor = Color.FromArgb(55, 55, 55)
            };
            Controls.Add(_hangmanBox);
        }



        private void CreateMenu()
        {
            _menuStrip = new MenuStrip
            {
                Dock = DockStyle.Top,
                BackColor = Color.FromArgb(55, 55, 55),
                RenderMode = ToolStripRenderMode.Professional, // belangrijk
                Renderer = new DarkToolStripRenderer() // prettige look
            };

            _menuSpel = new ToolStripMenuItem("Spel")
            {
                ForeColor = Color.Gray
            };

            _menuInstellingen = new ToolStripMenuItem("Instellingen")
            {
                ForeColor = Color.Black
            };
            _menuInstellingen.Click += MenuInstellingen_Click;

            _menuSpel.DropDownItems.Add(_menuInstellingen);
            _menuStrip.Items.Add(_menuSpel);

            Controls.Add(_menuStrip);
        }

        // Roep deze methode aan in de constructor (voor StartNewGame):
        private void LoadWordList()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "wordlist.txt");

            if (!File.Exists(path))
            {
                MessageBox.Show($"Bestand niet gevonden: {path}", "Fout", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
                return;
            }

            // Lees alle regels en filter op lengte
            _wordList = File.ReadAllLines(path)
                .Select(w => w.Trim())
                .Where(w => w.Length >= _minWordLength && w.Length <= _maxWordLength && w.All(c => char.IsLetter(c) || char.IsNumber(c) || c == '-'))
                .Select(w => w.ToUpperInvariant())
                .ToList();

            
            if (_wordList.Count == 0)
            {
                MessageBox.Show(
                    $"Geen geschikte woorden gevonden in wordlist.txt voor lengte " +
                    $"{_minWordLength}–{_maxWordLength}",
                    "Fout", MessageBoxButtons.OK, MessageBoxIcon.Error);
                // Niet meteen sluiten; laat gebruiker via Instellingen bijsturen
                // Close(); // <- liever niet automatisch sluiten
            }

        }

        public Galgje()
        {
            InitializeComponent();

            CreateMenu();          // <— nieuw

            CreateKeyboard();

            CreateWordBar();     // Nieuw

            StyleForm();

            // Zorg dat het formulier key events ontvangt
            KeyPreview = true;
            KeyDown += Galgje_KeyDown;

            
            _minWordLength = Properties.Settings.Default.MinWordLength;
            _maxWordLength = Properties.Settings.Default.MaxWordLength;


            CreateHangmanBox();
            LoadWordList();     // Nieuw: laad woordenlijst
            StartNewGame();      // Nieuw: meteen een spel starten

        }


        private void MenuInstellingen_Click(object? sender, EventArgs e)
        {
            using var dlg = new SettingsForm(_minWordLength, _maxWordLength);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _minWordLength = dlg.MinLength;
                _maxWordLength = dlg.MaxLength;

                // Herlaad de woordenlijst met de nieuwe filter
                LoadWordList();

                // Controle: zijn er nog woorden?
                if (_wordList.Count == 0)
                {
                    MessageBox.Show(
                        $"Geen woorden gevonden tussen lengte {_minWordLength} en {_maxWordLength}. " +
                        $"Pas de instellingen aan.",
                        "Geen woorden", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Start meteen een nieuw spel met deze instellingen
                StartNewGame();
            }

            
            Properties.Settings.Default.MinWordLength = _minWordLength;
            Properties.Settings.Default.MaxWordLength = _maxWordLength;

        }


        private void Galgje_Load(object sender, EventArgs e) { }
        private void StyleForm()
        {
            Text = "Galgje - Virtueel Toetsenbord";
            BackColor = Color.FromArgb(40, 40, 40);
            //FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1000, 470);
        }

        private void CreateKeyboard()
        {
            string[] rows =
            {
                "1 2 3 4 5 6 7 8 9 0 -", //0
                "Q W E R T Y U I O P", //1
                "A S D F G H J K L", //2
                "Z X C V B N M" //3
            };

            int startX = 30;
            int startY = 120;
            int buttonWidth = 50;
            int buttonHeight = 50;
            int spacing = 6;

            for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                var keys = rows[rowIndex].Split(' ');

                int offsetX = rowIndex * 25; // lichte verschuiving per rij

                for (int colIndex = 0; colIndex < keys.Length; colIndex++)
                {
                    var key = keys[colIndex];
                    var button = new Button
                    {
                        Text = key,
                        Width = buttonWidth,
                        Height = buttonHeight,
                        Left = startX + offsetX + colIndex * (buttonWidth + spacing),
                        Top = startY + rowIndex * (buttonHeight + spacing),
                        FlatStyle = FlatStyle.Flat,
                        Font = new Font("Segoe UI", 12, FontStyle.Bold),
                        ForeColor = Color.White,
                        BackColor = Color.FromArgb(70, 70, 70),
                        Tag = key
                    };

                    button.FlatAppearance.BorderSize = 1;
                    button.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 90);
                    button.FlatAppearance.MouseOverBackColor = Color.FromArgb(85, 85, 85);
                    button.FlatAppearance.MouseDownBackColor = Color.FromArgb(100, 100, 100);

                    button.Click += KeyButton_Click;

                    Controls.Add(button);
                    _keyboardButtons[key] = button;
                }
            }
        }

        // --- Nieuw: de woordbalk boven het toetsenbord ---
        private void CreateWordBar()
        {
            _wordBar = new Label
            {
                AutoSize = false,
                Left = 30,
                Top = _menuStrip?.Height + 8 ?? 33,  // dynamisch t.o.v. menu
                Width = ClientSize.Width - 60,
                Height = 40,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(55, 55, 55),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right

            };
            Controls.Add(_wordBar);
        }

        // --- Nieuw: start een nieuw spel ---
        private void StartNewGame()
        {
            if (_wordList.Count == 0)
                return;

            _targetWord = _wordList[_rng.Next(_wordList.Count)];


            _revealed = Enumerable.Range(0, _targetWord.Length).Select(_ => '_').ToArray();
            _guessed.Clear();

            _wrongGuesses = 0;
            DrawHangman();


            // Reset de keyboard-kleuren
            foreach (var btn in _keyboardButtons.Values)
            {
                btn.BackColor = Color.FromArgb(70, 70, 70);
                btn.Enabled = true;
            }

            UpdateWordBar();
        }

        private void DrawHangman()
        {
            Bitmap bmp = new Bitmap(_hangmanBox.Width, _hangmanBox.Height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(55, 55, 55));
                Pen pen = new Pen(Color.White, 3);

                // Teken poppetje afhankelijk van _wrongGuesses
                if (_wrongGuesses >= 1) g.DrawLine(pen, 10, 180, 150, 180); // bodem
                if (_wrongGuesses >= 2) g.DrawLine(pen, 40, 180, 40, 20); // verticale paal
                if (_wrongGuesses >= 3) g.DrawLine(pen, 40, 20, 120, 20); // horizontale balk
                if (_wrongGuesses >= 4) g.DrawLine(pen, 120, 20, 120, 40); // touw
                if (_wrongGuesses >= 5) g.DrawEllipse(pen, 100, 40, 40, 40); // hoofd
                if (_wrongGuesses >= 6) g.DrawLine(pen, 120, 80, 120, 130); // romp
                if (_wrongGuesses >= 7) g.DrawLine(pen, 120, 90, 90, 110); // arm links
                if (_wrongGuesses >= 8) g.DrawLine(pen, 120, 90, 150, 110); // arm rechts
                if (_wrongGuesses >= 9) g.DrawLine(pen, 120, 130, 100, 160); // been links
                if (_wrongGuesses >= 10) g.DrawLine(pen, 120, 130, 140, 160); // been rechts
            }

            _hangmanBox.Image = bmp;
        }

        // --- Nieuw: update de balk met _ en onthulde letters ---
        private void UpdateWordBar()
        {
            // Toon met spaties voor leesbaarheid: A _ _ Z _
            string display = string.Join(" ", _revealed);
            _wordBar.Text = display;
        }

        // --- Nieuw: verwerk een geraden letter ---
        private void ApplyGuess(char letter)
        {
            letter = char.ToUpperInvariant(letter);

            // als al geraden: niets doen
            if (_guessed.Contains(letter))
                return;

            _guessed.Add(letter);

            bool anyHit = false;
            for (int i = 0; i < _targetWord.Length; i++)
            {
                if (_targetWord[i] == letter)
                {
                    _revealed[i] = letter;
                    anyHit = true;
                }
            }

            // markeer toets rood en disable
            string key = letter.ToString();
            if (_keyboardButtons.TryGetValue(key, out var btn))
            {
                btn.BackColor = anyHit ? Color.Green : Color.Red;
                btn.Enabled = false;
            }
            UpdateWordBar();

            // check of woord compleet is
            if (!_revealed.Contains('_'))
            {
                // klaar!
                var res = MessageBox.Show(
                    $"Gefeliciteerd! Het woord was: {_targetWord}\n\nNieuw spel starten?",
                    "Galgje",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (res == DialogResult.Yes)
                    StartNewGame();
                else
                {
                    // Stop het spel
                    Close();
                }
            }

            if (!anyHit)
            {
                _wrongGuesses++;
                DrawHangman();

                if (_wrongGuesses >= 10)
                {
                    var res = MessageBox.Show($"Game Over! Het woord was: {_targetWord}\n\nNieuw spel starten?",
                        "Galgje",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information);

                    if (res == DialogResult.Yes)
                        StartNewGame();
                    else
                    {
                        Close();
                    }
                }
            }

        }

        private void KeyButton_Click(object? sender, EventArgs e)
        {
            if (sender is not Button btn)
                return;

            //HighlightKey(btn.Text);

            // Gebruik de klik ook als gok op de letter
            if (!string.IsNullOrWhiteSpace(btn.Text))
            {
                char c = btn.Text[0];
                ApplyGuess(c);
            }

        }

        private void Galgje_KeyDown(object? sender, KeyEventArgs e)
        {
            string key = e.KeyCode.ToString().ToUpper();

            // Zorg dat we alleen A–Z verwerken
            if (key.Length == 1 && key[0] >= 'A' && key[0] <= 'Z')
            {

                ApplyGuess(key[0]);
                e.Handled = true;
            }
            else if (key.Length == 2 && key[1] >= '0' && key[1] <= '9')
            {

                ApplyGuess(key[1]);
                e.Handled = true;
            }
            else if (key == "OEMMINUS")
            {
                ApplyGuess('-');
                e.Handled = true;
            }
        }

    }

    
public class DarkColorTable : ProfessionalColorTable
    {
        private readonly Color _border = Color.FromArgb(90, 90, 90);
        private readonly Color _bg = Color.FromArgb(55, 55, 55);      // menu achtergrond
        private readonly Color _dropBg = Color.FromArgb(50, 50, 50);  // dropdown achtergrond
        private readonly Color _hover = Color.FromArgb(75, 75, 75);
        private readonly Color _pressed = Color.FromArgb(85, 85, 85);

        public override Color MenuStripGradientBegin => _bg;
        public override Color MenuStripGradientEnd   => _bg;

        public override Color ToolStripDropDownBackground => _dropBg;
        public override Color ImageMarginGradientBegin    => _dropBg;
        public override Color ImageMarginGradientMiddle   => _dropBg;
        public override Color ImageMarginGradientEnd      => _dropBg;

        public override Color MenuItemSelected           => _hover;
        public override Color MenuItemSelectedGradientBegin => _hover;
        public override Color MenuItemSelectedGradientEnd   => _hover;

        public override Color MenuItemPressedGradientBegin => _pressed;
        public override Color MenuItemPressedGradientEnd   => _pressed;

        public override Color SeparatorDark => _border;
        public override Color SeparatorLight => _border;

        public override Color ToolStripBorder => _border;
        public override Color MenuBorder => _border;
    }

    public class DarkToolStripRenderer : ToolStripProfessionalRenderer
    {
        public DarkToolStripRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // Alle item-teksten wit
            e.TextColor = Color.White;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            // Zorg dat elke ToolStrip (inclusief dropdown) donker wordt
            using var b = new SolidBrush(Color.FromArgb(55, 55, 55));
            e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var bounds = new Rectangle(Point.Empty, e.Item.Bounds.Size);
            Color fill = e.Item.Selected ? Color.FromArgb(75, 75, 75)
                         : e.Item.Pressed ? Color.FromArgb(85, 85, 85)
                         : Color.FromArgb(55, 55, 55);
            using var b = new SolidBrush(fill);
            e.Graphics.FillRectangle(b, bounds);
        }
    }

}

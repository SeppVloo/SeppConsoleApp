using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SeppWinFormsApp
{

    using System;
    using System.Drawing;
    using System.Windows.Forms;

    namespace SeppWinFormsApp
    {
        public class SettingsForm : Form
        {
            private NumericUpDown _numMin;
            private NumericUpDown _numMax;
            private Button _btnOk;
            private Button _btnCancel;

            public int MinLength { get; private set; }
            public int MaxLength { get; private set; }

            public SettingsForm(int currentMin, int currentMax)
            {
                Text = "Instellingen – Woordlengte";
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                ClientSize = new Size(360, 180);
                BackColor = Color.FromArgb(40, 40, 40);

                var lblMin = new Label
                {
                    Text = "Minimum lengte:",
                    ForeColor = Color.White,
                    Left = 20,
                    Top = 20,
                    Width = 140
                };
                var lblMax = new Label
                {
                    Text = "Maximum lengte:",
                    ForeColor = Color.White,
                    Left = 20,
                    Top = 60,
                    Width = 140
                };

                _numMin = new NumericUpDown
                {
                    Left = 180,
                    Top = 15,
                    Width = 120,
                    Minimum = 1,
                    Maximum = 50,
                    Value = currentMin
                };
                _numMax = new NumericUpDown
                {
                    Left = 180,
                    Top = 55,
                    Width = 120,
                    Minimum = 1,
                    Maximum = 50,
                    Value = currentMax
                };

                _btnOk = new Button
                {
                    Text = "OK",
                    DialogResult = DialogResult.OK,
                    Left = 180,
                    Top = 110,
                    Width = 80,
                    BackColor = Color.FromArgb(70, 70, 70),
                    ForeColor = Color.White
                };
                
                _btnOk.FlatStyle = FlatStyle.Flat;
                _btnOk.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 90);
                _btnOk.FlatAppearance.MouseOverBackColor = Color.FromArgb(85, 85, 85);

                _btnCancel = new Button
                {
                    Text = "Annuleren",
                    DialogResult = DialogResult.Cancel,
                    Left = 270,
                    Top = 110,
                    Width = 80,
                    BackColor = Color.FromArgb(70, 70, 70),
                    ForeColor = Color.White
                };
                
                _btnCancel.FlatStyle = FlatStyle.Flat;
                _btnCancel.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 90);
                _btnCancel.FlatAppearance.MouseOverBackColor = Color.FromArgb(85, 85, 85);


                _btnOk.Click += (s, e) =>
                {
                    if (_numMin.Value > _numMax.Value)
                    {
                        MessageBox.Show("Minimum mag niet groter zijn dan maximum.",
                            "Ongeldige instelling", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        DialogResult = DialogResult.None; // blijf in dialoog
                        return;
                    }

                    MinLength = (int)_numMin.Value;
                    MaxLength = (int)_numMax.Value;
                };

                
                _numMin.BackColor = Color.FromArgb(60, 60, 60);
                _numMin.ForeColor = Color.White;
                _numMax.BackColor = Color.FromArgb(60, 60, 60);
                _numMax.ForeColor = Color.White;


                Controls.AddRange(new Control[] { lblMin, lblMax, _numMin, _numMax, _btnOk, _btnCancel });
                AcceptButton = _btnOk;
                CancelButton = _btnCancel;
            }
        }
    }
}

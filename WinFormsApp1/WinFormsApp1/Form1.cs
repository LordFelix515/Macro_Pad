using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MacroPad
{
    public partial class Form1 : Form
    {
        private SerialPort? _serialPort;

        int activekey = 1;

        // Hafıza Dizileri
        private readonly bool[] ctrl = new bool[11];
        private readonly bool[] alt = new bool[11];
        private readonly bool[] shift = new bool[11];
        private readonly bool[] win = new bool[11];
        private readonly string?[] key = new string?[11];
        private readonly string?[] fn = new string?[11];

        System.Windows.Forms.Timer connectionTimer = new System.Windows.Forms.Timer();
        bool isScanning = false;
        string activeProfileName = "";

        private System.Windows.Forms.Timer? notConnectTimer;
        private int notConnectElapsed = 0;
        private const int NotConnectTimeoutSeconds = 10;
        private bool portListRefreshed = false;

        private Macro_pad_Load? _macroPadForm;
        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _trayMenu;

        // ComboBox'taki özel görevler (Medya ve Navigasyon tuşları)
        private readonly string[] specialFunctions = new string[] {
            "Play/Pause", "Next Track", "Previous Track", "Mute",
            "Volume Up", "Volume Down", "BackSpace", "Enter",
            "Esc", "Tab", "Up", "Down", "Right", "Left",
            "PageUp", "PageDown", "Delete", "Insert"
        };

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            Hide();
            ShowInTaskbar = false;
            ShowMacroPadLoad(0);
            RefreshPortList();
            InitializeTray();
            if (this.Controls.Find("btnProfilYonet", true).FirstOrDefault() is Button btnYonet)
            {
                btnYonet.Click += (s, ev) => ShowProfileManagerDialog();
            }

            // Tüm tuşları otomatik bulup bağlayan zeki sistem
            OtomatikButonBagla();

            connectionTimer.Interval = 2000;
            connectionTimer.Tick += ConnectionTimer_Tick;
            connectionTimer.Start();

            panel2.MouseDown += panel2_MouseDown;

            if (this.Controls.Find("cmbProfiles", true).FirstOrDefault() is ComboBox cmb)
                cmb.SelectedIndexChanged += CmbProfiles_SelectedIndexChanged;

            if (this.Controls.Find("btnYeniProfil", true).FirstOrDefault() is Button btn)
                btn.Click += btnYeniProfil_Click;

            textBox1.TextChanged += (s, ev) => { if (activekey >= 1 && activekey <= 10) key[activekey] = textBox1.Text; };
            checkBox1.CheckedChanged += (s, ev) => { if (activekey >= 1 && activekey <= 10) ctrl[activekey] = checkBox1.Checked; };
            checkBox2.CheckedChanged += (s, ev) => { if (activekey >= 1 && activekey <= 10) alt[activekey] = checkBox2.Checked; };
            checkBox3.CheckedChanged += (s, ev) => { if (activekey >= 1 && activekey <= 10) shift[activekey] = checkBox3.Checked; };
            checkBox4.CheckedChanged += (s, ev) => { if (activekey >= 1 && activekey <= 10) win[activekey] = checkBox4.Checked; };
        }

        // --- AKILLI BUTON MOTORU ---
        private void OtomatikButonBagla()
        {
            for (int i = 1; i <= 10; i++)
            {
                Control[] btns = this.Controls.Find("button" + i, true);
                if (btns.Length > 0 && btns[0] is Button btn)
                {
                    btn.Click -= DinamikTus_Click;
                    btn.Click += DinamikTus_Click;
                }

                Control[] pbs = this.Controls.Find("pictureBox" + (i + 1), true);
                if (pbs.Length > 0 && pbs[0] is PictureBox pb)
                {
                    pb.Click -= DinamikPictureBox_Click;
                    pb.Click += DinamikPictureBox_Click;
                }
            }

            Control[] saveBtns = this.Controls.Find("button11", true);
            if (saveBtns.Length > 0 && saveBtns[0] is Button saveBtn)
            {
                saveBtn.Click -= button11_Click;
                saveBtn.Click += button11_Click;
            }
        }

        private void SaveCurrentKeyToMemory()
        {
            if (activekey >= 1 && activekey <= 10)
            {
                ctrl[activekey] = checkBox1.Checked;
                alt[activekey] = checkBox2.Checked;
                shift[activekey] = checkBox3.Checked;
                win[activekey] = checkBox4.Checked;
                key[activekey] = textBox1.Text;
                fn[activekey] = comboBox2.Text;
            }
        }

        private void DinamikTus_Click(object sender, EventArgs e)
        {
            if (sender is Button btn)
            {
                int idx = int.Parse(btn.Name.Replace("button", ""));
                SaveCurrentKeyToMemory();
                activekey = idx;
                activ();
            }
        }

        private void DinamikPictureBox_Click(object sender, EventArgs e)
        {
            if (sender is PictureBox pb)
            {
                int idx = int.Parse(pb.Name.Replace("pictureBox", "")) - 1;
                SaveCurrentKeyToMemory();
                activekey = idx;
                activ();
            }
        }

        // --- JSON PARSER (YENİ VE ESKİ FORMAT DESTEKLİ) ---
        private void ParseJsonToUI(string jsonStr)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(jsonStr))
                {
                    JsonElement root = doc.RootElement;

                    // ESKİ FORMAT KONTROLÜ (m1, m2 vb.)
                    if (root.TryGetProperty("m1", out _))
                    {
                        for (int i = 1; i <= 10; i++)
                        {
                            if (root.TryGetProperty("m" + i, out JsonElement mElem))
                            {
                                string[] parts = (mElem.GetString() ?? "").Split(',');
                                if (parts.Length >= 4)
                                {
                                    ctrl[i] = parts[0].Trim() == "1";
                                    alt[i] = parts[1].Trim() == "1";
                                    shift[i] = parts[2].Trim() == "1";
                                    win[i] = parts[3].Trim() == "1";
                                    key[i] = parts.Length >= 5 ? parts[4].Trim() : "";
                                    fn[i] = parts.Length >= 6 ? parts[5].Trim() : "Not Active";
                                    if (string.IsNullOrWhiteSpace(fn[i])) fn[i] = "Not Active";
                                }
                            }
                        }
                        return;
                    }

                    // YENİ FORMAT KONTROLÜ
                    if (root.TryGetProperty("tuslar", out JsonElement tuslar))
                    {
                        for (int i = 1; i <= 10; i++)
                        {
                            if (i - 1 < tuslar.GetArrayLength())
                            {
                                string gorev = tuslar[i - 1].GetProperty("gorev").GetString() ?? "YOK";
                                ctrl[i] = alt[i] = shift[i] = win[i] = false;
                                key[i] = "";

                                if (gorev == "YOK" || string.IsNullOrEmpty(gorev))
                                    fn[i] = "Not Active";
                                else if (gorev.StartsWith("STRING:"))
                                {
                                    fn[i] = "String";
                                    key[i] = gorev.Substring(7);
                                }
                                else if (specialFunctions.Contains(gorev))
                                    fn[i] = gorev; // Medya tuşu (Play/Pause vb.)
                                else
                                {
                                    fn[i] = "Key Combination";
                                    string[] parts = gorev.Split('+');
                                    foreach (var p in parts)
                                    {
                                        if (p == "CTRL" || p == "CONTROL") ctrl[i] = true;
                                        else if (p == "ALT") alt[i] = true;
                                        else if (p == "SHIFT") shift[i] = true;
                                        else if (p == "WIN" || p == "GUI") win[i] = true;
                                        else key[i] = p;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine("JSON Parse Hatası: " + ex.Message); }
        }

        private string BuildJsonFromUI()
        {
            var profileData = new { isim = activeProfileName.Replace(".json", ""), tuslar = new object[10] };

            for (int i = 1; i <= 10; i++)
            {
                string gorevStr = "YOK";

                if (fn[i] == "Not Active" || string.IsNullOrEmpty(fn[i])) gorevStr = "YOK";
                else if (fn[i] == "String") gorevStr = "STRING:" + key[i];
                else if (specialFunctions.Contains(fn[i])) gorevStr = fn[i]; // Play/Pause vb
                else
                {
                    List<string> parts = new List<string>();
                    if (ctrl[i]) parts.Add("CTRL");
                    if (alt[i]) parts.Add("ALT");
                    if (shift[i]) parts.Add("SHIFT");
                    if (win[i]) parts.Add("WIN");
                    if (!string.IsNullOrEmpty(key[i])) parts.Add(key[i].ToUpper());

                    if (parts.Count > 0) gorevStr = string.Join("+", parts);
                }
                profileData.tuslar[i - 1] = new { gorev = gorevStr };
            }
            return JsonSerializer.Serialize(profileData);
        }

        // --- EKRAN GÜNCELLEME MOTORU ---
        private void activ()
        {
            if (fn[activekey] == "Key Combination")
            {
                checkBox1.Enabled = true; checkBox2.Enabled = true;
                checkBox3.Enabled = true; checkBox4.Enabled = true;
                textBox1.Enabled = true;
            }
            else if (fn[activekey] == "String")
            {
                checkBox1.Enabled = false; checkBox2.Enabled = false;
                checkBox3.Enabled = false; checkBox4.Enabled = false;
                textBox1.Enabled = true;
            }
            else // Play/Pause vs seçilirse her şey kitlenir!
            {
                checkBox1.Enabled = false; checkBox2.Enabled = false;
                checkBox3.Enabled = false; checkBox4.Enabled = false;
                textBox1.Enabled = false;
            }

            label2.Text = "Macro " + activekey.ToString();

            // comboBox değerini değiştirirken eventi kapat ki sonsuz döngü olmasın
            comboBox2.SelectedIndexChanged -= comboBox2_SelectedIndexChanged;
            comboBox2.Text = fn[activekey] ?? "Not Active";
            comboBox2.SelectedIndexChanged += comboBox2_SelectedIndexChanged;

            checkBox1.Checked = ctrl[activekey];
            checkBox2.Checked = alt[activekey];
            checkBox3.Checked = shift[activekey];
            checkBox4.Checked = win[activekey];
            textBox1.Text = key[activekey];

            // Resim Güncelleme
            pictureBox2.Image = Properties.Resources.Button; pictureBox3.Image = Properties.Resources.Button;
            pictureBox4.Image = Properties.Resources.Button; pictureBox5.Image = Properties.Resources.Button;
            pictureBox6.Image = Properties.Resources.Button; pictureBox7.Image = Properties.Resources.Button;
            pictureBox8.Image = Properties.Resources.Button; pictureBox9.Image = Properties.Resources.Button;
            pictureBox10.Image = Properties.Resources.Button; pictureBox11.Image = Properties.Resources.Button;

            if (activekey == 1) pictureBox2.Image = Properties.Resources.Button_Pressed;
            else if (activekey == 2) pictureBox3.Image = Properties.Resources.Button_Pressed;
            else if (activekey == 3) pictureBox4.Image = Properties.Resources.Button_Pressed;
            else if (activekey == 4) pictureBox5.Image = Properties.Resources.Button_Pressed;
            else if (activekey == 5) pictureBox6.Image = Properties.Resources.Button_Pressed;
            else if (activekey == 6) pictureBox7.Image = Properties.Resources.Button_Pressed;
            else if (activekey == 7) pictureBox8.Image = Properties.Resources.Button_Pressed;
            else if (activekey == 8) pictureBox9.Image = Properties.Resources.Button_Pressed;
            else if (activekey == 9) pictureBox10.Image = Properties.Resources.Button_Pressed;
            else if (activekey == 10) pictureBox11.Image = Properties.Resources.Button_Pressed;
        }

        private void comboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
            fn[activekey] = comboBox2.Text;
            activ();
        }

        private void button11_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(activeProfileName))
            {
                MessageBox.Show("Lütfen önce bir profil seçin veya oluşturun."); return;
            }

            SaveCurrentKeyToMemory();
            string jsonVeri = BuildJsonFromUI();

            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.WriteLine($"SAVE_PROFILE:{activeProfileName}|{jsonVeri}");
            }
            else MessageBox.Show("Cihazla seri bağlantı kurulamadı!");
        }

        // --- ARKA PLAN VE BAĞLANTI (ÇÖKMEYİ ENGELLEYEN ASYNC SİSTEM) ---
        private void RefreshPortList()
        {
            _macroPadForm?.SetProgress(20);
            comboBox1.Items.Clear();
            portListRefreshed = true;

            if (notConnectTimer == null)
            {
                notConnectTimer = new System.Windows.Forms.Timer() { Interval = 1000 };
                notConnectTimer.Tick += NotConnectTimer_Tick;
            }
            notConnectElapsed = 0;
            notConnectTimer.Start();

            try
            {
                var ports = SerialPort.GetPortNames().Distinct().ToArray();
                Array.Sort(ports);
                comboBox1.Items.AddRange(ports);
                if (comboBox1.Items.Count > 0) comboBox1.SelectedIndex = 0;
                _macroPadForm?.SetProgress(25);
            }
            catch { }
        }

        private void NotConnectTimer_Tick(object? sender, EventArgs e)
        {
            notConnectElapsed++;
            if (notConnectElapsed >= NotConnectTimeoutSeconds)
            {
                notConnectTimer?.Stop();
                portListRefreshed = false;
                _macroPadForm?.SetLabelText("Cihaz bulunamadı veya cevap vermiyor.");
            }
        }

        private async void ConnectionTimer_Tick(object sender, EventArgs e)
        {
            if (_serialPort != null && _serialPort.IsOpen) return;
            if (isScanning) return;

            isScanning = true;
            await AutoConnectAsync();
            isScanning = false;
        }

        private async Task AutoConnectAsync()
        {
            string[] ports = SerialPort.GetPortNames();
            Invoke(new Action(() => _macroPadForm?.SetProgress(50)));

            foreach (string port in ports)
            {
                if (_serialPort != null && _serialPort.IsOpen && _serialPort.PortName == port) continue;

                Invoke(new Action(() => {
                    _macroPadForm?.SetProgress(60);
                    _macroPadForm?.SetLabelText($"Port aranıyor: {port}");
                }));

                bool deviceFound = await Task.Run(() =>
                {
                    try
                    {
                        using (var tempPort = new SerialPort(port, 115200) { Encoding = System.Text.Encoding.UTF8 })
                        {
                            tempPort.ReadTimeout = 500;
                            tempPort.WriteTimeout = 500;
                            tempPort.NewLine = "\n";
                            tempPort.RtsEnable = true;
                            tempPort.DtrEnable = true;

                            try
                            {
                                tempPort.Open();
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Could not open temp port {port}: {ex.Message}");
                                return false;
                            }

                            try
                            {
                                // give device time to reset/respond
                                System.Threading.Thread.Sleep(300);
                                try { tempPort.DiscardInBuffer(); } catch { }
                                try { tempPort.DiscardOutBuffer(); } catch { }

                                tempPort.WriteLine("WHO_ARE_YOU");

                                var sw = System.Diagnostics.Stopwatch.StartNew();
                                int timeoutMillis = 2000;
                                string acc = string.Empty;
                                int lastPing = 0;

                                while (sw.ElapsedMilliseconds < timeoutMillis)
                                {
                                    try
                                    {
                                        var part = tempPort.ReadExisting();
                                        if (!string.IsNullOrEmpty(part))
                                        {
                                            acc += part;
                                            if (acc.Contains("MACRO_PAD"))
                                            {
                                                return true;
                                            }
                                        }
                                    }
                                    catch (Exception readEx)
                                    {
                                        Debug.WriteLine($"Read error on {port}: {readEx.Message}");
                                        break;
                                    }

                                    // Cihaz uyanma aşamasındaysa her 400ms'de bir pingi tekrarla
                                    if (sw.ElapsedMilliseconds - lastPing >= 400)
                                    {
                                        lastPing = (int)sw.ElapsedMilliseconds;
                                        try { tempPort.WriteLine("WHO_ARE_YOU"); } catch { }
                                    }

                                    System.Threading.Thread.Sleep(50);
                                }

                                Debug.WriteLine($"Port {port} did not respond in {timeoutMillis}ms.");
                            }
                            finally
                            {
                                try { if (tempPort.IsOpen) tempPort.Close(); } catch { }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error scanning port {port}: {ex.Message}");
                    }

                    return false;
                });

                if (deviceFound)
                {
                    Invoke(new Action(() =>
                    {
                        _macroPadForm?.SetLabelText("Cihaz verileri alınıyor.");
                        _macroPadForm?.SetProgress(80);

                        if (!comboBox1.Items.Contains(port)) comboBox1.Items.Add(port);
                        comboBox1.SelectedItem = port;
                        buttonConnect_Click(null, null);

                        label1.Text = "Cihaz Algılandı: " + port;
                    }));
                    break;
                }
            }
        }

        private void buttonConnect_Click(object sender, EventArgs e)
        {
            if (portListRefreshed) { notConnectElapsed = 0; notConnectTimer?.Stop(); portListRefreshed = false; }
            buttonConnect.Enabled = false;
            try
            {
                if (_serialPort == null || !_serialPort.IsOpen)
                {
                    if (comboBox1.SelectedItem == null) return;
                    var portName = comboBox1.SelectedItem.ToString();

                    _serialPort = new SerialPort(portName, 115200) 
                    { 
                        NewLine = "\n", 
                        ReadTimeout = 500, 
                        DtrEnable = true, 
                        RtsEnable = true, 
                        Encoding = System.Text.Encoding.UTF8 
                    };
                    _serialPort.DataReceived += SerialPort_DataReceived;
                    _serialPort.ErrorReceived += SerialPort_ErrorReceived;
                    _serialPort.Open();

                    try { _serialPort.DiscardInBuffer(); } catch { }
                    connected();
                    buttonConnect.Text = "Bağlantıyı Kes";

                    _serialPort.WriteLine("GET_PROFILES");
                }
                else
                {
                    if (_serialPort.IsOpen) _serialPort.Close();
                    if (_serialPort != null)
                    {
                        _serialPort.DataReceived -= SerialPort_DataReceived;
                        _serialPort.ErrorReceived -= SerialPort_ErrorReceived;
                        _serialPort.Dispose();
                        _serialPort = null;
                    }
                    buttonConnect.Text = "Bağlan";
                    RefreshPortList();
                }
            }
            finally { buttonConnect.Enabled = true; }
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string data = ((SerialPort)sender).ReadLine().Trim();
                Invoke(new Action(() =>
                {
                    if (data.StartsWith("PROFILE_LIST:"))
                    {
                        // Veriyi parçala (Liste ve Aktif Profil)
                        string[] anaParcalar = data.Substring(13).Split('|');
                        string listeKismi = anaParcalar[0];
                        string aktifProfilKismi = anaParcalar.Length > 1 ? anaParcalar[1].Replace("ACTIVE:", "") : "";

                        Control[] controls = this.Controls.Find("cmbProfiles", true);
                        if (controls.Length > 0 && controls[0] is ComboBox cmbProfiles)
                        {
                            // KRİTİK: Otomatik seçim tetiklenmesin diye event'i geçici olarak kapatıyoruz
                            cmbProfiles.SelectedIndexChanged -= CmbProfiles_SelectedIndexChanged;

                            cmbProfiles.Items.Clear();
                            if (!string.IsNullOrEmpty(listeKismi))
                            {
                                string[] profiles = listeKismi.Split(',');
                                foreach (var p in profiles)
                                {
                                    cmbProfiles.Items.Add(p.Replace(".json", ""));
                                }

                                // Cihazda o an hangi profil aktifse onu bul ve ComboBox'ta seç
                                string temizAktifAd = aktifProfilKismi.Replace(".json", "").Trim();
                                int aktifIndex = cmbProfiles.FindStringExact(temizAktifAd);

                                if (aktifIndex != -1)
                                    cmbProfiles.SelectedIndex = aktifIndex;
                                else if (cmbProfiles.Items.Count > 0)
                                    cmbProfiles.SelectedIndex = 0;

                                if (cmbProfiles.SelectedItem != null)
                                {
                                    activeProfileName = cmbProfiles.SelectedItem.ToString() + ".json";
                                    if (_serialPort != null && _serialPort.IsOpen)
                                    {
                                        _serialPort.WriteLine($"GET_PROFILE:{activeProfileName}");
                                    }
                                }
                            }

                            // İşlem bitince event'i tekrar açıyoruz
                            cmbProfiles.SelectedIndexChanged += CmbProfiles_SelectedIndexChanged;
                        }
                    }
                    else if (data.StartsWith("PROFILE_DATA:"))
                    {
                        string jsonStr = data.Substring(13).Trim();
                        if (jsonStr != "ERROR" && !string.IsNullOrEmpty(jsonStr)) 
                        { 
                            ParseJsonToUI(jsonStr); 
                            activ(); 
                        }
                    }
                    else if (data == "SAVE_SUCCESS")
                    {
                        MessageBox.Show("Ayarlar cihaza başarıyla kaydedildi!", "Başarılı", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }));
            }
            catch { }
        }

        private void SerialPort_ErrorReceived(object sender, SerialErrorReceivedEventArgs e) { }
        // PROFİL SEÇİLDİĞİNDE CİHAZA HABER VER
        private void CmbProfiles_SelectedIndexChanged(object sender, EventArgs e)
        {
            Control[] controls = this.Controls.Find("cmbProfiles", true);
            if (controls.Length > 0 && controls[0] is ComboBox cmbProfiles && cmbProfiles.SelectedItem != null)
            {
                // Kullanıcının gördüğü isim "Oyun", biz arkasına ".json" ekleyip değişkene atıyoruz
                activeProfileName = cmbProfiles.SelectedItem.ToString() + ".json";

                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.WriteLine($"GET_PROFILE:{activeProfileName}");
                    System.Threading.Thread.Sleep(50);
                    _serialPort.WriteLine($"SET_PROFILE:{activeProfileName}");
                }
            }
        }

        // YENİ PROFİL OLUŞTURMA (İsim Sorarak)
        private void btnYeniProfil_Click(object sender, EventArgs e)
        {
            string profilAdi = ShowInputDialog("Yeni profil için bir isim girin (Örn: Oyun, Tasarim):", "Yeni Profil Oluştur");

            if (!string.IsNullOrWhiteSpace(profilAdi))
            {
                // Kullanıcı yanlışlıkla .json yazarsa temizle
                profilAdi = profilAdi.Replace(".json", "").Trim();

                Control[] controls = this.Controls.Find("cmbProfiles", true);
                if (controls.Length > 0 && controls[0] is ComboBox cmbProfiles)
                {
                    if (!cmbProfiles.Items.Contains(profilAdi))
                    {
                        cmbProfiles.Items.Add(profilAdi);
                        cmbProfiles.SelectedItem = profilAdi; // Bu satır otomatik activeProfileName'i .json'lu yapar

                        for (int i = 1; i <= 10; i++)
                        {
                            fn[i] = "Not Active";
                            key[i] = "";
                            ctrl[i] = alt[i] = shift[i] = win[i] = false;
                        }

                        button11_Click(null, null); // Cihaza boş kaydet
                        activ();
                    }
                    else
                    {
                        MessageBox.Show("Bu isimde bir profil zaten var!", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }
        // 1. KENDİ ÖZEL KARANLIK TEMA INPUT PENCEREMİZ (Referans gerektirmez!)
        private string ShowInputDialog(string metin, string baslik)
        {
            Form prompt = new Form()
            {
                Width = 400,
                Height = 160,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = baslik,
                StartPosition = FormStartPosition.CenterScreen,
                MaximizeBox = false,
                BackColor = Color.FromArgb(45, 45, 48), // Senin temana uygun koyu renk
                ForeColor = Color.White
            };
            Label textLabel = new Label() { Left = 20, Top = 20, Text = metin, AutoSize = true };
            TextBox textBox = new TextBox() { Left = 20, Top = 50, Width = 340, BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White };
            Button confirmation = new Button() { Text = "Tamam", Left = 260, Width = 100, Top = 80, DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(70, 70, 70) };

            prompt.Controls.Add(textLabel);
            prompt.Controls.Add(textBox);
            prompt.Controls.Add(confirmation);
            prompt.AcceptButton = confirmation;

            return prompt.ShowDialog() == DialogResult.OK ? textBox.Text : "";
        }

        // PROFİL YÖNETİM PANELİ (Tamamen Kodla Üretilmiş Özel Tasarım)
        private void ShowProfileManagerDialog()
        {
            Form manager = new Form()
            {
                Width = 420,
                Height = 320,
                Text = "Profil Yöneticisi",
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false
            };

            ListBox list = new ListBox()
            {
                Left = 20,
                Top = 20,
                Width = 220,
                Height = 230,
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10)
            };

            // Ana ekrandaki profilleri bu listeye çek (json uzantısız halleri zaten ekli)
            Control[] controls = this.Controls.Find("cmbProfiles", true);
            if (controls.Length > 0 && controls[0] is ComboBox cmbProfiles)
            {
                foreach (var item in cmbProfiles.Items) list.Items.Add(item);
            }

            Button btnRename = new Button() { Text = "✎ Ad Değiştir", Left = 260, Top = 20, Width = 120, Height = 40, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(70, 70, 70) };
            Button btnDelete = new Button() { Text = "🗑 Sil", Left = 260, Top = 70, Width = 120, Height = 40, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(180, 60, 60) }; // Kırmızımsı silme butonu
            Button btnClose = new Button() { Text = "Kapat", Left = 260, Top = 210, Width = 120, Height = 40, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(70, 70, 70) };

            // SİLME İŞLEMİ
            btnDelete.Click += (s, e) =>
            {
                if (list.SelectedItem == null) { MessageBox.Show("Önce listeden bir profil seçin."); return; }

                string secili = list.SelectedItem.ToString();

                // EMİN MİSİNİZ DİYALOG KUTUSU
                var cevap = MessageBox.Show($"'{secili}' profilini kalıcı olarak silmek istediğinize emin misiniz?", "Profili Sil", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (cevap == DialogResult.Yes)
                {
                    // 1. Cihaza silme komutu gönder
                    if (_serialPort != null && _serialPort.IsOpen)
                        _serialPort.WriteLine($"DELETE_PROFILE:{secili}.json");

                    // 2. Listeden ve Ana Ekranoan Kaldır
                    list.Items.Remove(secili);
                    if (controls.Length > 0 && controls[0] is ComboBox cmb)
                    {
                        cmb.Items.Remove(secili);
                        if (cmb.Items.Count > 0) cmb.SelectedIndex = 0; else activeProfileName = "";
                    }
                }
            };

            // YENİDEN ADLANDIRMA İŞLEMİ
            btnRename.Click += (s, e) =>
            {
                if (list.SelectedItem == null) { MessageBox.Show("Önce listeden bir profil seçin."); return; }

                string secili = list.SelectedItem.ToString();
                string yeniAd = ShowInputDialog($"'{secili}' profili için yeni bir isim girin:", "Yeniden Adlandır");

                if (!string.IsNullOrWhiteSpace(yeniAd))
                {
                    yeniAd = yeniAd.Replace(".json", "").Trim(); // Uzantı girerse temizle

                    // 1. Cihaza ad değiştirme komutu gönder
                    if (_serialPort != null && _serialPort.IsOpen)
                        _serialPort.WriteLine($"RENAME_PROFILE:{secili}.json|{yeniAd}.json");

                    // 2. Listeyi ve Ana Ekranı Güncelle
                    int idx = list.SelectedIndex;
                    list.Items[idx] = yeniAd;

                    if (controls.Length > 0 && controls[0] is ComboBox cmb)
                    {
                        int cmbIdx = cmb.Items.IndexOf(secili);
                        if (cmbIdx >= 0) cmb.Items[cmbIdx] = yeniAd;
                        cmb.SelectedItem = yeniAd;
                        activeProfileName = yeniAd + ".json";
                    }
                }
            };

            btnClose.Click += (s, e) => manager.Close();

            manager.Controls.Add(list);
            manager.Controls.Add(btnRename);
            manager.Controls.Add(btnDelete);
            manager.Controls.Add(btnClose);

            manager.ShowDialog(); // Paneli Ekrana Çıkar
        }
        // --- FORM, TRAY VE KAPATMA İŞLEMLERİ ---
        private void connected() { _macroPadForm?.SetProgress(100); ShowInTaskbar = true; this.Show(); this.BringToFront(); _macroPadForm?.Dispose(); }
        private void ShowMacroPadLoad(int? initialProgress = null, bool modal = false)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => ShowMacroPadLoad(initialProgress, modal))); return; }
            if (_macroPadForm == null || _macroPadForm.IsDisposed) _macroPadForm = new Macro_pad_Load();
            if (initialProgress.HasValue) _macroPadForm.SetProgress(initialProgress.Value);
            if (modal) _macroPadForm.ShowDialog(this); else _macroPadForm.Show();
        }
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        private const int WM_NCLBUTTONDOWN = 0xA1; private const int HTCAPTION = 0x2;
        private void panel2_MouseDown(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(this.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0); } }
        private void pictureBox12_Click(object sender, EventArgs e) { DisposeTray(); this.Close(); }
        private void pictureBox13_Click(object sender, EventArgs e) { Hide(); ShowInTaskbar = false; _notifyIcon?.ShowBalloonTip(1000, "Macro Pad", "Uygulama arka plana alındı.", ToolTipIcon.Info); }
        private void InitializeTray()
        {
            _trayMenu = new ContextMenuStrip(); _trayMenu.Items.Add("Aç", null, (s, e) => RestoreFromTray()); _trayMenu.Items.Add("Çıkış", null, (s, e) => { Close(); });
            _notifyIcon = new NotifyIcon { Icon = this.Icon, Text = "Macro Pad", ContextMenuStrip = _trayMenu, Visible = true };
            _notifyIcon.DoubleClick += (s, e) => RestoreFromTray(); this.Resize += Form1_Resize;
        }
        private void RestoreFromTray() { if (InvokeRequired) { BeginInvoke((Action)RestoreFromTray); return; } Show(); ShowInTaskbar = true; WindowState = FormWindowState.Normal; BringToFront(); }
        private void Form1_Resize(object? sender, EventArgs e) { if (WindowState == FormWindowState.Minimized) { Hide(); _notifyIcon?.ShowBalloonTip(1000, "Macro Pad", "Uygulama arka plana alındı.", ToolTipIcon.Info); } }
        private void DisposeTray() { try { if (_notifyIcon != null) { _notifyIcon.Visible = false; _notifyIcon.Dispose(); } _trayMenu?.Dispose(); } catch { } }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_serialPort != null) { try { if (_serialPort.IsOpen) _serialPort.Close(); } catch { } finally { _serialPort.Dispose(); _serialPort = null; } }
            base.OnFormClosing(e);
        }
        // --- VISUAL STUDIO TASARIMCI (DESIGNER) HATALARINI ÖNLEMEK İÇİN ---

        private void button1_Click(object sender, EventArgs e) { SaveCurrentKeyToMemory(); activekey = 1; activ(); }
        private void button2_Click(object sender, EventArgs e) { SaveCurrentKeyToMemory(); activekey = 2; activ(); }
        private void button3_Click(object sender, EventArgs e) { SaveCurrentKeyToMemory(); activekey = 3; activ(); }
        private void button4_Click(object sender, EventArgs e) { SaveCurrentKeyToMemory(); activekey = 4; activ(); }
        private void button5_Click(object sender, EventArgs e) { SaveCurrentKeyToMemory(); activekey = 5; activ(); }
        private void button6_Click(object sender, EventArgs e) { SaveCurrentKeyToMemory(); activekey = 6; activ(); }
        private void button7_Click(object sender, EventArgs e) { SaveCurrentKeyToMemory(); activekey = 7; activ(); }
        private void button8_Click(object sender, EventArgs e) { SaveCurrentKeyToMemory(); activekey = 8; activ(); }
        private void button9_Click(object sender, EventArgs e) { SaveCurrentKeyToMemory(); activekey = 9; activ(); }
        private void button10_Click(object sender, EventArgs e) { SaveCurrentKeyToMemory(); activekey = 10; activ(); }

        // Eğer tasarımcıda CheckBox veya ComboBox eventleri de hata verirse diye:
        private void checkBox1_CheckedChanged(object sender, EventArgs e) { }
        private void checkBox2_CheckedChanged(object sender, EventArgs e) { }
        private void comboBox1_DropDown(object sender, EventArgs e) { RefreshPortList(); }
    }
}
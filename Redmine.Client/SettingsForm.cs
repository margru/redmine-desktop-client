using System;
using System.Configuration;
using System.Windows.Forms;
using System.Collections.Generic;
using Redmine.Client.Languages;
using Redmine.Net.Api.Types;
using Redmine.Net.Api.Extensions;
using Redmine.Net.Api.Serialization;
using Redmine.Client.Properties;
using System.Collections.Specialized;

namespace Redmine.Client
{
    public partial class SettingsForm : Form
    {
        private List<System.Globalization.CultureInfo> supportedLang = new List<System.Globalization.CultureInfo> {
            new System.Globalization.CultureInfo("nl"),
            new System.Globalization.CultureInfo("en"),
            new System.Globalization.CultureInfo("de"),
            new System.Globalization.CultureInfo("cs-CZ"),
            new System.Globalization.CultureInfo("pt-BR"),
            new System.Globalization.CultureInfo("fr"),
            new System.Globalization.CultureInfo("gl"),
            new System.Globalization.CultureInfo("ru"),
            new System.Globalization.CultureInfo("pl"),
            new System.Globalization.CultureInfo("es-MX"),
            new System.Globalization.CultureInfo("zh-CN")
        };

        /* api version lower then 1.1 does not support time-entry, so is not supported. */

        private List<IdentifiableName> apiVersions = new List<IdentifiableName> {
            /*ClientExtensionMethods.Named((int)ApiVersion.V10x, LangTools.GetTextForApiVersion(ApiVersion.V10x)),*/
            ClientExtensionMethods.Named((int)ApiVersion.V11x, LangTools.GetTextForApiVersion(ApiVersion.V11x)),
            ClientExtensionMethods.Named((int)ApiVersion.V12x, LangTools.GetTextForApiVersion(ApiVersion.V12x)),
            ClientExtensionMethods.Named((int)ApiVersion.V13x, LangTools.GetTextForApiVersion(ApiVersion.V13x)),
            ClientExtensionMethods.Named((int)ApiVersion.V14x, LangTools.GetTextForApiVersion(ApiVersion.V14x)),
            ClientExtensionMethods.Named((int)ApiVersion.V20x, LangTools.GetTextForApiVersion(ApiVersion.V20x)),
            ClientExtensionMethods.Named((int)ApiVersion.V21x, LangTools.GetTextForApiVersion(ApiVersion.V21x)),
            ClientExtensionMethods.Named((int)ApiVersion.V22x, LangTools.GetTextForApiVersion(ApiVersion.V22x)),
            ClientExtensionMethods.Named((int)ApiVersion.V23x, LangTools.GetTextForApiVersion(ApiVersion.V23x)),
            ClientExtensionMethods.Named((int)ApiVersion.V24x, LangTools.GetTextForApiVersion(ApiVersion.V24x)),
            ClientExtensionMethods.Named((int)ApiVersion.V25x, LangTools.GetTextForApiVersion(ApiVersion.V25x)),
            // Everything from 2.4 on uses the same feature set (see the ApiVersion enum); these
            // are listed only so the picker can match a modern server. Major versions only - no
            // need to enumerate every minor release.
            ClientExtensionMethods.Named((int)ApiVersion.V30x, LangTools.GetTextForApiVersion(ApiVersion.V30x)),
            ClientExtensionMethods.Named((int)ApiVersion.V40x, LangTools.GetTextForApiVersion(ApiVersion.V40x)),
            ClientExtensionMethods.Named((int)ApiVersion.V50x, LangTools.GetTextForApiVersion(ApiVersion.V50x)),
            ClientExtensionMethods.Named((int)ApiVersion.V60x, LangTools.GetTextForApiVersion(ApiVersion.V60x))
        };

        public SettingsForm()
        {
            InitializeComponent();
            LoadLanguage();

            supportedLang.Sort((x, y) => string.Compare(x.DisplayName, y.DisplayName));

            LanguageComboBox.DataSource = supportedLang;
            LanguageComboBox.ValueMember = "Name";
            LanguageComboBox.DisplayMember = "DisplayName";

            RedmineVersionComboBox.DataSource = apiVersions;
            RedmineVersionComboBox.ValueMember = "Id";
            RedmineVersionComboBox.DisplayMember = "Name";

            LoadConfig();
            EnableDisableAuthenticationFields();
        }

        private void LoadLanguage()
        {
            LangTools.UpdateControlsForLanguage(this.Controls);
            this.Text = Lang.DlgSettingsTitle;
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            Uri uri;
            if (!Uri.TryCreate(RedmineBaseUrlTextBox.Text, UriKind.Absolute, out uri))
            {
                MessageBox.Show(Lang.Error_InvalidUrl, Lang.Error, MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                this.RedmineBaseUrlTextBox.Focus();
                return;
            }
            SaveConfig();
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void SaveConfig()
        {
            try
            {
                Languages.Lang.Culture = (System.Globalization.CultureInfo)LanguageComboBox.SelectedItem;
            }
            catch (Exception) { }
            if (Languages.Lang.Culture == null)
                Languages.Lang.Culture = new System.Globalization.CultureInfo("en");
            try
            {
                Settings.Default.UpdateSetting("RedmineURL", RedmineBaseUrlTextBox.Text);
                Settings.Default.UpdateSetting("RedmineUser", RedmineUsernameTextBox.Text);
                Settings.Default.UpdateSetting("RedminePassword", RedminePasswordTextBox.Text);
                Settings.Default.UpdateSetting("RedmineAuthentication", AuthenticationCheckBox.Checked);
                if (radioButtonJson.Checked)
                    Settings.Default.UpdateSetting("CommunicationType", SerializationType.Json);
                else
                    Settings.Default.UpdateSetting("CommunicationType", SerializationType.Xml);

                Settings.Default.UpdateSetting("CheckForUpdates", CheckForUpdatesCheckBox.Checked);
                Settings.Default.UpdateSetting("MinimizeToSystemTray", MinimizeToSystemTrayCheckBox.Checked);
                Settings.Default.UpdateSetting("MinimizeOnStartTimer", MinimizeOnStartTimerCheckBox.Checked);
                Settings.Default.UpdateSetting("PopupInterval", PopupTimout.Value);
                Settings.Default.UpdateSetting("CacheLifetime", CacheLifetime.Value);
                Settings.Default.UpdateSetting("LanguageCode", Languages.Lang.Culture.Name);
                Settings.Default.UpdateSetting("ApiVersion", (int)RedmineVersionComboBox.SelectedValue);
                Settings.Default.UpdateSetting("PauseTickingOnLock", PauseTimerOnLockCheckBox.Checked);
                if (ComboBoxCloseStatus.Enabled)
                    Settings.Default.UpdateSetting("ClosedStatus", (int)ComboBoxCloseStatus.SelectedValue);
                if (UpdateIssueIfStateCheckBox.Enabled)
                {
                    Settings.Default.UpdateSetting("UpdateIssueIfNew", UpdateIssueIfStateCheckBox.Checked);
                    if (UpdateIssueIfStateCheckBox.Checked)
                    {
                        Settings.Default.UpdateSetting("NewStatus", (int)UpdateIssueNewStateComboBox.SelectedValue);
                        Settings.Default.UpdateSetting("InProgressStatus", (int)UpdateIssueInProgressComboBox.SelectedValue);
                    }
                }
                Settings.Default.UpdateSetting("AddNoteOnChangeStatus", AddNoteOnChangeCheckBox.Checked);
                Settings.Default.UpdateSetting("OnlyMyProjects", OnlyShowMyProjects.Checked);
                Settings.Default.Save();
                String Name = Settings.Default.LanguageCode;
                Enumerations.SaveAll();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Stop);
            }
        }

        private void LoadConfig()
        {
            RedmineBaseUrlTextBox.Text = Settings.Default.RedmineURL;
            AuthenticationCheckBox.Checked = Settings.Default.RedmineAuthentication;
            RedmineUsernameTextBox.Text = Settings.Default.RedmineUser;
            RedminePasswordTextBox.Text = Settings.Default.RedminePassword;
            radioButtonJson.Checked = Settings.Default.CommunicationType == SerializationType.Json;
            radioButtonXml.Checked = Settings.Default.CommunicationType != SerializationType.Json;

            MinimizeToSystemTrayCheckBox.Checked = Settings.Default.MinimizeToSystemTray;
            MinimizeOnStartTimerCheckBox.Checked = Settings.Default.MinimizeOnStartTimer;
            CheckForUpdatesCheckBox.Checked = Settings.Default.CheckForUpdates;
            CacheLifetime.Value = Settings.Default.CacheLifetime;
            PopupTimout.Value = Settings.Default.PopupInterval;
            PauseTimerOnLockCheckBox.Checked = Settings.Default.PauseTickingOnLock;
            RedmineVersionComboBox.SelectedIndex = RedmineVersionComboBox.FindStringExact(Languages.LangTools.GetTextForApiVersion((ApiVersion)Settings.Default.ApiVersion));
            UpdateIssueIfStateCheckBox.Checked = Settings.Default.UpdateIssueIfNew;
            try {
                Languages.Lang.Culture = new System.Globalization.CultureInfo(Settings.Default.LanguageCode);
            }
            catch (Exception)
            {
                Languages.Lang.Culture = System.Globalization.CultureInfo.CurrentUICulture;
            }
            LanguageComboBox.SelectedIndex = LanguageComboBox.FindStringExact(Languages.Lang.Culture.DisplayName);
            AddNoteOnChangeCheckBox.Checked = Settings.Default.AddNoteOnChangeStatus;
            OnlyShowMyProjects.Checked = Settings.Default.OnlyMyProjects;
        }

        private SerializationType GetSelectedMimeFormat()
        {
            return radioButtonXml.Checked ? SerializationType.Xml : SerializationType.Json;
        }

        private void AuthenticationCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            EnableDisableAuthenticationFields();
        }

        private void EnableDisableAuthenticationFields()
        {
            if (AuthenticationCheckBox.Checked)
            {
                RedmineUsernameTextBox.Enabled = true;
                RedminePasswordTextBox.Enabled = true;
            }
            else
            {
                RedmineUsernameTextBox.Enabled = false;
                RedminePasswordTextBox.Enabled = false;
            }
        }

        private void BtnEditActivitiesButton_Click(object sender, EventArgs e)
        {
            EditEnumListForm dlg = new EditEnumListForm(Enumerations.Activities, Lang.EnumName_Activities);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                Enumerations.Activities = dlg.enumeration;
                Enumerations.SaveActivities();
            }
        }

        private void BtnEditIssuePriorities_Click(object sender, EventArgs e)
        {
            EditEnumListForm dlg = new EditEnumListForm(Enumerations.IssuePriorities, Lang.EnumName_IssuePriorities);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                Enumerations.IssuePriorities = dlg.enumeration;
                Enumerations.SaveIssuePriorities();
            }
        }

        private void BtnTestConnection_Click(object sender, EventArgs e)
        {
            try
            {
                Redmine.Net.Api.RedmineManager manager;
                if (AuthenticationCheckBox.Checked)
                    manager = ClientExtensionMethods.CreateManager(RedmineBaseUrlTextBox.Text, RedmineUsernameTextBox.Text, RedminePasswordTextBox.Text, GetSelectedMimeFormat());
                else
                    manager = ClientExtensionMethods.CreateManager(RedmineBaseUrlTextBox.Text, null, null, GetSelectedMimeFormat());
                User newCurrentUser = manager.GetCurrentUser();
                MessageBox.Show(Lang.ConnectionTestOK_Text, Lang.ConnectionTestOK_Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(String.Format(Lang.ConnectionTestFailed_Text, ex.Message), Lang.ConnectionTestFailed_Title, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            LoadAndEnableCloseStatus();
            LoadAndEnableSetToInProgressStatus();
        }

        private IList<IssueStatus> CloseStatuses;

        private void LoadAndEnableCloseStatus()
        {
            labelSelectCloseStatus.Enabled = false;
            ComboBoxCloseStatus.Enabled = false;
            if ((ApiVersion)RedmineVersionComboBox.SelectedValue < ApiVersion.V13x)
                return;
            try
            {
                Redmine.Net.Api.RedmineManager manager;
                CloseStatuses = new List<IssueStatus>();
                if (AuthenticationCheckBox.Checked)
                    manager = ClientExtensionMethods.CreateManager(RedmineBaseUrlTextBox.Text, RedmineUsernameTextBox.Text, RedminePasswordTextBox.Text, GetSelectedMimeFormat());
                else
                    manager = ClientExtensionMethods.CreateManager(RedmineBaseUrlTextBox.Text, null, null, GetSelectedMimeFormat());

                CloseStatuses = manager.Get<IssueStatus>();
                ComboBoxCloseStatus.DataSource = CloseStatuses;
                ComboBoxCloseStatus.ValueMember = "Id";
                ComboBoxCloseStatus.DisplayMember = "Name";
                labelSelectCloseStatus.Enabled = true;
                ComboBoxCloseStatus.Enabled = true;

                if (Settings.Default.ClosedStatus != 0)
                    ComboBoxCloseStatus.SelectedValue = Settings.Default.ClosedStatus;
                else
                    ComboBoxCloseStatus.SelectedIndex = ComboBoxCloseStatus.FindStringExact("Closed");


            }
            catch (Exception ex)
            {
                MessageBox.Show(String.Format(Lang.Error_Exception, ex.Message), Lang.Error, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                ComboBoxCloseStatus.Enabled = false;
                labelSelectCloseStatus.Enabled = false;
            }
        }

        private List<IssueStatus> NewStatuses;
        private List<IssueStatus> InProgressStatuses;

        private void LoadAndEnableSetToInProgressStatus()
        {
            UpdateIssueNewStateComboBox.Enabled = false;
            UpdateIssueInProgressComboBox.Enabled = false;
            UpdateIssueIfStateCheckBox.Enabled = false;
            UpdateIssueIfStateLabel.Enabled = false;
            BtnEditActivitiesButton.Enabled = true;
            BtnEditIssuePriorities.Enabled = true;
            if ((ApiVersion)RedmineVersionComboBox.SelectedValue < ApiVersion.V13x)
                return;
            try
            {
                Redmine.Net.Api.RedmineManager manager;
                NewStatuses = new List<IssueStatus>();
                InProgressStatuses = new List<IssueStatus>();
                if (AuthenticationCheckBox.Checked)
                    manager = ClientExtensionMethods.CreateManager(RedmineBaseUrlTextBox.Text, RedmineUsernameTextBox.Text, RedminePasswordTextBox.Text, GetSelectedMimeFormat());
                else
                    manager = ClientExtensionMethods.CreateManager(RedmineBaseUrlTextBox.Text, null, null, GetSelectedMimeFormat());

                NameValueCollection parameters = new NameValueCollection { { "is_closed", "false" } };
                foreach (IssueStatus status in manager.Get<IssueStatus>(parameters.ToOptions()))
                {
                    if (!status.IsClosed)
                    {
                        NewStatuses.Add(status);
                        InProgressStatuses.Add(status);
                    }
                }
                UpdateIssueNewStateComboBox.DataSource = NewStatuses;
                UpdateIssueNewStateComboBox.ValueMember = "Id";
                UpdateIssueNewStateComboBox.DisplayMember = "Name";

                if (Settings.Default.NewStatus!= 0)
                    UpdateIssueNewStateComboBox.SelectedValue = Settings.Default.NewStatus;
                else
                    UpdateIssueNewStateComboBox.SelectedIndex = UpdateIssueNewStateComboBox.FindStringExact("New");

                UpdateIssueInProgressComboBox.DataSource = InProgressStatuses;
                UpdateIssueInProgressComboBox.ValueMember = "Id";
                UpdateIssueInProgressComboBox.DisplayMember = "Name";

                if (Settings.Default.InProgressStatus != 0)
                    UpdateIssueInProgressComboBox.SelectedValue = Settings.Default.InProgressStatus;
                else
                    UpdateIssueInProgressComboBox.SelectedIndex = UpdateIssueInProgressComboBox.FindStringExact("In Progress");

                UpdateIssueIfStateCheckBox.Enabled = true;
                UpdateIssueIfStateCheckBox.Checked = Settings.Default.UpdateIssueIfNew;
                UpdateIssueIfStateLabel.Enabled = true;

                EnableDisableUpdateIssueIfNewFields();
            }
            catch (Exception ex)
            {
                MessageBox.Show(String.Format(Lang.Error_Exception, ex.Message), Lang.Error, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                UpdateIssueNewStateComboBox.Enabled = false;
                UpdateIssueInProgressComboBox.Enabled = false;
            }
            if ((ApiVersion)RedmineVersionComboBox.SelectedValue < ApiVersion.V22x)
                return;

            BtnEditActivitiesButton.Enabled = false;
            BtnEditIssuePriorities.Enabled = false;
        }

        private void EnableDisableUpdateIssueIfNewFields()
        {
            UpdateIssueNewStateComboBox.Enabled = UpdateIssueIfStateCheckBox.Checked && UpdateIssueIfStateCheckBox.Enabled;
            UpdateIssueInProgressComboBox.Enabled = UpdateIssueIfStateCheckBox.Checked && UpdateIssueIfStateCheckBox.Enabled;
        }

        private void UpdateIssueIfStateCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            EnableDisableUpdateIssueIfNewFields();
        }

    }
}

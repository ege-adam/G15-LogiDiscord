using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LogiFrame;
using Dec.DiscordIPC;
using Dec.DiscordIPC.Commands;
using Dec.DiscordIPC.Events;
using Dec.DiscordIPC.Entities;
using System.Text.Json;
using WindowsInput;

namespace LogiDiscordApplet
{
    class Program
    {
        private static readonly string CLIENT_ID = "***";
        private static readonly string CLIENT_SECRET = "***";
        private static readonly int bgAfter = 1000;

        private static DiscordIPC client;

        private static LCDApp lcdApp;
        private static LCDPicture lcdMicPic;
        private static LCDPicture lcdHeadsetPic;
        private static LCDMarquee lcdConnectedChannel;

        private static VoiceStateCreate.Data[] currentConnectedUsers;
        private static LCDUser[] usersOrdered;

        private static Dictionary<string, LCDUser> idLCDUserPairs = new Dictionary<string, LCDUser>();

        private static GetSelectedVoiceChannel.Data connectedChanneldata;

        private static string lastChannelID;
        private static string clientUserID;
        private static DateTime bgTime;

        private static TrayHelper trayHelper;

        private static InputSimulator inputSimulator;
        private static KeyboardSimulator keyboardSimulator;

        public static LCDApp LcdApp { get => lcdApp; }

        static async Task Main()
        {
            idLCDUserPairs = new Dictionary<string, LCDUser>();

            trayHelper = new TrayHelper();

            inputSimulator = new InputSimulator();
            keyboardSimulator = new KeyboardSimulator(inputSimulator);

            // Create a control.
            lcdMicPic = new LCDPicture
            {
                Location = new Point(5, 35),
                Size = new Size(8, 8),
                Image = ImagesRes.microphone_icon,
            };

            lcdConnectedChannel = new LCDMarquee
            {
                Location = new Point(22, 35),
                Size = new Size(LCDApp.DefaultSize.Width - 44, 8),
                Text = "Updating status...",
            };

            lcdHeadsetPic = new LCDPicture
            {
                Location = new Point(147, 35),
                Size = new Size(8, 8),
                Image = ImagesRes.headset_icon,
            };

            // Create an app instance.
            lcdApp = new LCDApp("LogiDiscord", false, false, false);

            lcdApp.Controls.Add(lcdMicPic);
            lcdApp.Controls.Add(lcdHeadsetPic);
            lcdApp.Controls.Add(lcdConnectedChannel);

            lcdApp.PushToForeground();

            lcdConnectedChannel.Text = "Finding Discord";
            client = new DiscordIPC(CLIENT_ID);

            try
            {
                await client.InitAsync();
            }
            catch(IOException)
            {
                lcdConnectedChannel.Text = "Discord not found, trying to launch it...";
                System.Diagnostics.Process.Start("discord://");
                Thread.Sleep(2000);
                try
                {
                    await client.InitAsync();
                }
                catch(IOException)
                {
                    bool discordFound = false;
                    while (!discordFound)
                    {
                        lcdConnectedChannel.Text = "Discord could not found, waiting..";
                        Thread.Sleep(2000);

                        try
                        {
                            await client.InitAsync();
                        }
                        finally
                        {
                            discordFound = true;
                        }
                    }
                }
            }

            lcdConnectedChannel.Text = "Check Discord for authorization request";
            string accessToken;
            try
            {
                Authorize.Data codeResponse = await client.SendCommandAsync(
                    new Authorize.Args()
                    {
                        scopes = new List<string>() { "rpc", "identify", "rpc.voice.read", "rpc.voice.write" },
                        client_id = CLIENT_ID
                    });
                accessToken = await OauthHelper.GetAccessTokenAsync(codeResponse.code, CLIENT_ID, CLIENT_SECRET);
            }
            catch (ErrorResponseException)
            {
                lcdConnectedChannel.Text = "Authorization denied.";
                return;
            }

            // Authenticate (ignoring the response here)
            Authenticate.Data authData = await client.SendCommandAsync(new Authenticate.Args()
            {
                access_token = accessToken
            });

            clientUserID = authData.user.id;
            AddUser(authData.user.id, authData.user.username, authData.user.avatar);

            await SubscribeEvents();

            GetSelectedVoiceChannel.Data voiceChannelData = await client.SendCommandAsync(new GetSelectedVoiceChannel.Args());
            if (voiceChannelData != null && voiceChannelData.id != null)
            {
                VoiceChannelSelect.Data fakeData = new VoiceChannelSelect.Data
                {
                    channel_id = voiceChannelData.id,
                    guild_id = voiceChannelData.guild_id
                };

                await OnVoiceChannelSelectAsync(fakeData);
                await CheckVoiceSettingsStatus();
            }

            trayHelper.onAppExit += new EventHandler(OnMenuExitClick);
            await UpdateUsersAsync();

            // A blocking call. Waits for the LCDApp instance to be disposed. (optional)
            lcdApp.WaitForClose();
        }

        private static async Task CheckVoiceSettingsStatus()
        {
            GetVoiceSettings.Data voiceSettingsData = await client.SendCommandAsync(new GetVoiceSettings.Args());
            if (voiceSettingsData != null)
            {
                if (voiceSettingsData.mute.GetValueOrDefault(false))
                {
                    lcdMicPic.Image = ImagesRes.microphone_icon_disabled;
                }
                else
                {
                    lcdMicPic.Image = ImagesRes.microphone_icon;
                }

                if (voiceSettingsData.deaf.GetValueOrDefault(false))
                {
                    lcdHeadsetPic.Image = ImagesRes.headset_icon_disabled;
                }
                else
                {
                    lcdHeadsetPic.Image = ImagesRes.headset_icon;
                }
            }
        }

        private static async Task SubscribeEvents()
        {
            EventHandler<VoiceChannelSelect.Data> voiceChannelSelectHandler = async (sender, data) => await OnVoiceChannelSelectAsync(data);
            client.OnVoiceChannelSelect += voiceChannelSelectHandler;
            await client.SubscribeAsync(new VoiceChannelSelect.Args());

            EventHandler<VoiceConnectionStatus.Data> voiceConnectionStatusHandler = async (sender, data) => await OnVoiceConnectionStatusAsync(data);
            client.OnVoiceConnectionStatus += voiceConnectionStatusHandler;
            await client.SubscribeAsync(new VoiceConnectionStatus.Args());

            EventHandler<VoiceStateUpdate.Data> voiceStateUpdateHandler = (sender, data) => OnVoiceStateUpdate(data);
            client.OnVoiceStateUpdate += voiceStateUpdateHandler;

            EventHandler<SpeakingStart.Data> speakingStartHandler = async (sender, data) => await OnTalkingUpdateAsync(true, data);
            client.OnSpeakingStart += speakingStartHandler;

            EventHandler<SpeakingStop.Data> speakingStopHandler = async (sender, data) => await OnTalkingUpdateAsync(false, data);
            client.OnSpeakingStop += speakingStopHandler;

            EventHandler<VoiceStateCreate.Data> voiceStateCreateHandler = async (sender, data) => await UpdateUsersAsync();
            client.OnVoiceStateCreate += voiceStateCreateHandler;

            EventHandler<VoiceStateDelete.Data> voiceStateDeleteHandler = async (sender, data) => await UpdateUsersAsync();
            client.OnVoiceStateDelete += voiceStateDeleteHandler;

            EventHandler<ButtonEventArgs> buttonHandler = async (sender, data) => await OnPressButtonAsync(data);
            lcdApp.ButtonPress += buttonHandler;
        }

        private static async Task OnPressButtonAsync(ButtonEventArgs _data)
        {
            if (!lcdApp.Visible) return;

            switch (_data.Button)
            {
                case 0:
                    keyboardSimulator.ModifiedKeyStroke(new[] { WindowsInput.Native.VirtualKeyCode.LCONTROL, WindowsInput.Native.VirtualKeyCode.LSHIFT }, new[] { WindowsInput.Native.VirtualKeyCode.MULTIPLY });
                    break;
                case 3:
                    keyboardSimulator.ModifiedKeyStroke(new[] { WindowsInput.Native.VirtualKeyCode.LCONTROL, WindowsInput.Native.VirtualKeyCode.LSHIFT }, new[] { WindowsInput.Native.VirtualKeyCode.SUBTRACT });
                    break;
                default:
                    return;
            }

            await Task.Delay(1);
        }

        private static void OnMenuExitClick(object sender, EventArgs e)
        {
            client.Dispose();
        }

        private static async Task OnTalkingUpdateAsync(bool talking, SpeakingStart.Data data)
        {
            if (data == null || data.user_id == null) return;
            if (!idLCDUserPairs.ContainsKey(data.user_id)) await UpdateUsersAsync();
            if (!idLCDUserPairs.ContainsKey(data.user_id)) return;

            idLCDUserPairs[data.user_id].IsTalking = talking;

            UpdateUI();
        }

        private static async Task UpdateChannel()
        {
            connectedChanneldata = await client.SendCommandAsync(new GetSelectedVoiceChannel.Args());
            lcdConnectedChannel.Text = "Connected: " + connectedChanneldata.name;
        }

        private static void UpdateUI()
        {
            usersOrdered = idLCDUserPairs.Values.ToArray();
            usersOrdered = usersOrdered.OrderByDescending(x => x.LastTimeTalked).ToArray();

            int count = 0;
            foreach (LCDUser _user in usersOrdered)
            {
                if (count > 3)
                {
                    _user.IsVisible = false;
                    continue;
                }

                _user.IsVisible = true;
                _user.SetLocation(new Point(0, count * 8));
                count++;
            }
        }

        private static async Task OnVoiceChannelSelectAsync(VoiceChannelSelect.Data data)
        {
            if (data == null || data.channel_id == null || data.channel_id.Length < 2) return;

            if(lastChannelID != null && lastChannelID.Length > 0)
            { 
                await client.UnsubscribeAsync(new VoiceStateUpdate.Args()
                {
                    channel_id = lastChannelID
                });

                await client.UnsubscribeAsync(new SpeakingStart.Args()
                {
                    channel_id = lastChannelID
                });

                await client.UnsubscribeAsync(new SpeakingStop.Args()
                {
                    channel_id = lastChannelID
                });

                await client.UnsubscribeAsync(new VoiceStateCreate.Args()
                {
                    channel_id = lastChannelID
                });

                await client.UnsubscribeAsync(new VoiceStateDelete.Args()
                {
                    channel_id = lastChannelID
                });
            }

            lastChannelID = data.channel_id;


            await client.SubscribeAsync(new VoiceStateUpdate.Args()
            {
                channel_id = data.channel_id
            });

            await client.SubscribeAsync(new SpeakingStart.Args()
            {
                channel_id = data.channel_id
            });

            await client.SubscribeAsync(new SpeakingStop.Args()
            {
                channel_id = data.channel_id
            });

            await client.SubscribeAsync(new VoiceStateCreate.Args()
            {
                channel_id = lastChannelID
            });

            await client.SubscribeAsync(new VoiceStateDelete.Args()
            {
                channel_id = lastChannelID
            });

            await UpdateChannel();
            await UpdateUsersAsync();
        }

        private static void OnVoiceStateUpdate(VoiceStateUpdate.Data data)
        {
            if (data == null || data.voice_state == null) return;

            if(data.voice_state.mute.GetValueOrDefault(false) || data.voice_state.self_mute.GetValueOrDefault(false))
            {
                lcdMicPic.Image = ImagesRes.microphone_icon_disabled;
            }
            else
            {
                lcdMicPic.Image = ImagesRes.microphone_icon;
            }

            if(data.voice_state.deaf.GetValueOrDefault(false) || data.voice_state.self_deaf.GetValueOrDefault(false))
            {
                lcdHeadsetPic.Image = ImagesRes.headset_icon_disabled;
            }
            else
            {
                lcdHeadsetPic.Image = ImagesRes.headset_icon;
            }
        }

        private static async Task OnVoiceConnectionStatusAsync(VoiceConnectionStatus.Data data)
        {
            if (data == null || data.state == null)
            {
                lcdConnectedChannel.Text = "- Disconnected -";
                return;
            }

            if (data.state.Equals("VOICE_CONNECTED"))
            {
                GetSelectedVoiceChannel.Data connectedChdata = await client.SendCommandAsync(new GetSelectedVoiceChannel.Args());
                if (connectedChdata == null)
                {
                    lcdConnectedChannel.Text = "- Disconnected -";
                    return;
                }

                VoiceChannelSelect.Data tempData = new VoiceChannelSelect.Data
                {
                    channel_id = lastChannelID,
                    guild_id = connectedChdata.guild_id
                };

                lastChannelID = connectedChdata.id;

                //await UpdateChannel(); Channel will be updated with OnVoiceChannelSelectAsync
                await OnVoiceChannelSelectAsync(tempData);
            }
            else
            {
                lcdConnectedChannel.Text = "- Disconnected -";
            }
        }

        private static async Task UpdateUsersAsync()
        {
            if (connectedChanneldata == null) await UpdateChannel();
            if (connectedChanneldata == null) return;

            UpdateUsers(connectedChanneldata.voice_states.ToArray());
        }

        private static void UpdateUsers(VoiceStateCreate.Data[] voiceStates)
        {
            if (voiceStates == null || voiceStates.Length == 0) return;
            if (currentConnectedUsers != null && currentConnectedUsers.Equals(voiceStates)) return;

            currentConnectedUsers = voiceStates;

            List<String> obseleteUsersID = new List<string>(idLCDUserPairs.Keys);
            List<VoiceStateCreate.Data> connectedUsersList = new List<VoiceStateCreate.Data>(currentConnectedUsers);

            foreach (string _obseleteUserID in obseleteUsersID)
            {

                if (_obseleteUserID.Equals(clientUserID)) continue; // Don't check host user

                if (connectedUsersList.FirstOrDefault(x => x.user.id.Equals(obseleteUsersID)) == null)
                {
                    if (!idLCDUserPairs.ContainsKey(_obseleteUserID)) continue;
                    idLCDUserPairs[_obseleteUserID].DestroyUI();
                    idLCDUserPairs.Remove(_obseleteUserID);
                }
            }

            foreach (VoiceStateCreate.Data userData in currentConnectedUsers)
            {
                AddUser(userData);
            }

            UpdateUI();
        }

        private static void AddUser(VoiceStateCreate.Data userData)
        {
            if (userData == null) return;
            AddUser(userData.user.id, userData.nick, userData.user.avatar);
        }

        private static void AddUser(string id, string username, string avatar)
        {
            if (idLCDUserPairs.ContainsKey(id)) return;
            idLCDUserPairs.Add(id, new LCDUser(id, username, avatar));
        }
    }
}

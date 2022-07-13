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

        private static VoiceStateCreate.Data[] lastUsers;
        private static Dictionary<string, LCDUser> idUserPairs;

        private static string lastChannelID;
        private static string clientUserID;
        private static DateTime bgTime;

        private static TrayHelper trayHelper;

        private static InputSimulator inputSimulator;
        private static KeyboardSimulator keyboardSimulator;

        public static LCDApp LcdApp { get => lcdApp; }

        static async Task Main()
        {
            idUserPairs = new Dictionary<string, LCDUser>();

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


            await UpdateUsers();

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

            EventHandler<VoiceStateCreate.Data> voiceStateCreateHandler = async (sender, data) => await UpdateUsers();
            client.OnVoiceStateCreate += voiceStateCreateHandler;

            EventHandler<VoiceStateDelete.Data> voiceStateDeleteHandler = async (sender, data) => await UpdateUsers();
            client.OnVoiceStateDelete += voiceStateDeleteHandler;

            EventHandler<ButtonEventArgs> buttonHandler = (sender, data) => OnPressButtonAsync(data);
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

        }

        private static void OnMenuExitClick(object sender, EventArgs e)
        {
            client.Dispose();
            Environment.Exit(Environment.ExitCode);
        }

        private static async Task OnTalkingUpdateAsync(bool talking, SpeakingStart.Data data)
        {
            if (data == null || data.user_id == null) return;
            if (!idUserPairs.ContainsKey(data.user_id)) await UpdateUsers();
            if (!idUserPairs.ContainsKey(data.user_id)) return;

            idUserPairs[data.user_id].IsTalking = talking;

            await UpdateUIAsync();
        }

        private static async Task UpdateUIAsync()
        {
            GetSelectedVoiceChannel.Data connectedChanneldata = await client.SendCommandAsync(new GetSelectedVoiceChannel.Args());
            lcdConnectedChannel.Text = "Connected: " + connectedChanneldata.name;

            LCDUser[] usersOrdered = idUserPairs.Values.ToArray();
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

            PushForegroundForAMoment();

            await UpdateUsers();
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

                await OnVoiceChannelSelectAsync(tempData);
            }
            else
            {
                lcdConnectedChannel.Text = "- Disconnected -";
            }
        }

        private static async Task UpdateUsers()
        {
            GetSelectedVoiceChannel.Data data = await client.SendCommandAsync(new GetSelectedVoiceChannel.Args());
            if(data == null) return;

            await UpdateUsers(data.voice_states.ToArray());
        }

        private static async Task UpdateUsers(VoiceStateCreate.Data[] voiceStates)
        {
            if (voiceStates == null || voiceStates.Length == 0) return;
            if (lastUsers != null && lastUsers.Equals(voiceStates)) return;

            lastUsers = voiceStates;

            if (idUserPairs == null) idUserPairs = new Dictionary<string, LCDUser>();

            List<String> fakeUsersKey = new List<string>(idUserPairs.Keys);
            List<VoiceStateCreate.Data> fakeUsers = new List<VoiceStateCreate.Data>(lastUsers);

            bool found;
            foreach (string _userKey in fakeUsersKey)
            {

                if (_userKey.Equals(clientUserID)) continue;

                found = false;

                foreach (VoiceStateCreate.Data data in fakeUsers)
                {
                    if (_userKey.Equals(data.user.id))
                    {
                        fakeUsers.Remove(data);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    if (!idUserPairs.ContainsKey(_userKey)) continue;
                    idUserPairs[_userKey].Destroy();
                    idUserPairs.Remove(_userKey);
                }
            }

            foreach (VoiceStateCreate.Data userData in lastUsers)
            {
                AddUser(userData);
            }

            await UpdateUIAsync();

            PushForegroundForAMoment();
        }

        private static void PushForegroundForAMoment()
        {
            /* if (LCDApp.)
            {
                lcdApp.PushToForeground();
                SendToBackground();
            } */

            return;
        }

        private static void AddUser(VoiceStateCreate.Data userData)
        {
            if (userData == null) return;
            AddUser(userData.user.id, userData.nick, userData.user.avatar);
        }

        private static void AddUser(string id, string username, string avatar)
        {
            if (idUserPairs.ContainsKey(id)) return;
            idUserPairs.Add(id, new LCDUser(id, username, avatar));
        }

        private static void SendToBackground()
        {
            Task.Factory.StartNew(() =>
            {
                bgTime = DateTime.Now.AddSeconds(bgAfter / 1000);
                while (bgTime > DateTime.Now)
                {
                    Thread.Sleep(bgAfter / 4);
                }
                lcdApp.PushToBackground();
            });
        }
    }
}

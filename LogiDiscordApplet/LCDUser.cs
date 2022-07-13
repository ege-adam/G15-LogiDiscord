using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LogiFrame;

namespace LogiDiscordApplet
{
    public class LCDUser
    {
        private string id;
        private string username;
        private string avatarURL;

        private LCDMarquee lcdTitle;
        private LCDPicture lcdPicture;
        private LCDPicture lcdPictureBorder;
        private DateTime lastTimeTalked;

        private bool isTalking;
        private bool isVisible;

        public string ID { get => id; }
        public string Username { get => username; }
        public LCDMarquee LcdTitle { get => lcdTitle; }
        public LCDPicture LcdPicture { get => lcdPicture; }
        public DateTime LastTimeTalked { get => lastTimeTalked; }

        public bool IsTalking { get => isTalking; set { isTalking = value; lastTimeTalked = DateTime.Now; lcdPicture.Visible = (!value) && isVisible; lcdPictureBorder.Visible = value && isVisible; } }

        public bool IsVisible { get => isVisible; set { isVisible = value; lcdPicture.Visible = value && !isTalking; lcdPictureBorder.Visible = value && isTalking; lcdTitle.Visible = value; } }

        public LCDUser(string id, string username, string avatar)
        {
            this.id = id;
            this.username = username;

            avatarURL = "https://cdn.discordapp.com/avatars/" + id + "/" + avatar + ".png" + "?size=16";
            lastTimeTalked = DateTime.Now;
            Console.WriteLine(avatarURL);

            lcdPicture = new LCDPicture
            {
                Location = new Point(0, 0),
                Size = new Size(8, 8), // Resources.gtech is the image we want to draw on the screen.
                Image = ImageTools.ResizeImage(ImageTools.GetImageFromURL(avatarURL), new Size(8,8)),
            };

            lcdPictureBorder = new LCDPicture
            {
                Location = new Point(0, 0),
                Size = new Size(8, 8), // Resources.gtech is the image we want to draw on the screen.
                Image = ImageTools.ResizeImage(ImageTools.GetImageFromURL(avatarURL), new Size(8, 8), true),
            };

            lcdTitle = new LCDMarquee
            {
                Location = new Point(10, 0),
                Size = new Size(LCDApp.DefaultSize.Width - 10, 8),
                Text = username,
            };

            IsVisible = true;

            Program.LcdApp.Controls.Add(lcdPicture);
            Program.LcdApp.Controls.Add(lcdPictureBorder);
            Program.LcdApp.Controls.Add(lcdTitle);
        }
        ~LCDUser()
        {
            Destroy();
        }

        public void SetLocation(Point _point)
        {
            lcdPicture.Location = _point;
            lcdPictureBorder.Location = _point;
            lcdTitle.Location = new Point(lcdPicture.Location.X + 10, _point.Y);
        }

        public void Destroy()
        {
            IsVisible = false;
            lcdPicture.Visible = false;
            lcdTitle.Visible = false;
            lcdPictureBorder.Visible = false;

            Program.LcdApp.Controls.Remove(lcdPicture);
            Program.LcdApp.Controls.Remove(lcdPictureBorder);
            Program.LcdApp.Controls.Remove(lcdTitle);
        }
    }

}

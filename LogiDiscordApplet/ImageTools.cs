using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace LogiDiscordApplet
{
    class ImageTools
    {

        public static Image GetImageFromURL(string imageUrl)
        {
            using (WebClient webClient = new WebClient())
            {
                byte[] data = webClient.DownloadData(imageUrl);

                using (MemoryStream ms = new MemoryStream(data))
                {
                    return Image.FromStream(ms);
                }

            }
        }

        public static Image ResizeImage(System.Drawing.Image imgToResize, Size size, bool addBorders = false)
        {
            //Get the image current width  
            int sourceWidth = imgToResize.Width;
            //Get the image current height  
            int sourceHeight = imgToResize.Height;
            float nPercent = 0;
            float nPercentW = 0;
            float nPercentH = 0;
            //Calulate  width with new desired size  
            nPercentW = ((float)size.Width / (float)sourceWidth);
            //Calculate height with new desired size  
            nPercentH = ((float)size.Height / (float)sourceHeight);
            if (nPercentH < nPercentW)
                nPercent = nPercentW;
            else
                nPercent = nPercentH;
            //New Width  
            int destWidth = (int)(sourceWidth * nPercent);
            //New Height  
            int destHeight = (int)(sourceHeight * nPercent);
            Bitmap b = new Bitmap(destWidth, destHeight);
            Graphics g = Graphics.FromImage((System.Drawing.Image)b);
            g.InterpolationMode = InterpolationMode.Default;
            // Draw image with new width and height  
            g.DrawImage(imgToResize, 0, 0, destWidth, destHeight);
            if(addBorders) g.DrawRectangle(new Pen(Brushes.Black, 1), new Rectangle(0, 0, destWidth, destHeight));
            g.Dispose();
            return (System.Drawing.Image)b;
        }
    }
}

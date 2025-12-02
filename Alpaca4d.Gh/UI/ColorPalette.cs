using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Drawing;

namespace Alpaca4d.UI
{
    internal static class Palette
    {

        // Transparency factor for colors
        public static double TransparencyFactor = 0.5;

        // Tech Colors
        public static Color DarkTech => System.Drawing.Color.FromArgb((int)(255 * TransparencyFactor), 42, 45, 49);
        public static Color LightGrey => System.Drawing.Color.FromArgb((int)(255 * TransparencyFactor), 244, 244, 244);

        // Alpaca4d Brand Colors (from logo)
        public static Color AlpacaRed => System.Drawing.Color.FromArgb((int)(255 * TransparencyFactor), 254, 0, 0);        // Red from head/torso
        public static Color AlpacaOrange => System.Drawing.Color.FromArgb((int)(255 * TransparencyFactor), 235, 108, 63);     // Orange from head/neck
        public static Color AlpacaPurple => System.Drawing.Color.FromArgb((int)(255 * TransparencyFactor), 158, 53, 218);    // Purple from mid-body
        public static Color AlpacaLightGreen => System.Drawing.Color.FromArgb((int)(255 * TransparencyFactor), 175, 201, 48); // Light green from hindquarters
        public static Color AlpacaLightBlue => System.Drawing.Color.FromArgb((int)(255 * TransparencyFactor), 66, 136, 247);  // Light blue from hindquarters
        public static Color AlpacaDarkBlue => System.Drawing.Color.FromArgb((int)(255 * TransparencyFactor), 1, 30, 254);     // Dark blue from tail

        // Force Diagrams Colors
        public static Color N_Positive => System.Drawing.Color.FromArgb((int)(255 * TransparencyFactor), 254, 0, 0);
        public static Color N_Negative => Color.FromArgb((int)(255 * TransparencyFactor), 1, 30, 254);
        public static Color Vy_Positive => System.Drawing.Color.FromArgb((int)(255 * TransparencyFactor), 235, 108, 63);
        public static Color Vy_Negative => System.Drawing.Color.FromArgb((int)(255 * TransparencyFactor), 66, 136, 247);
        public static Color Vz_Positive => Color.FromArgb((int)(255 * TransparencyFactor), 158, 53, 218);
        public static Color Vz_Negative => System.Drawing.Color.FromArgb((int)(255 * TransparencyFactor), 175, 201, 48);
        public static Color Torsion_Positive => System.Drawing.Color.FromArgb((int)(255 * TransparencyFactor), 254, 0, 0);
        public static Color Torsion_Negative => System.Drawing.Color.FromArgb((int)(255 * TransparencyFactor), 1, 30, 254);
        public static Color My_Positive => Color.FromArgb((int)(255 * TransparencyFactor), 235, 108, 63);
        public static Color My_Negative => Color.FromArgb((int)(255 * TransparencyFactor), 66, 136, 247);
        public static Color Mz_Positive => Color.FromArgb((int)(255 * TransparencyFactor), 158, 53, 218);
        public static Color Mz_Negative => Color.FromArgb((int)(255 * TransparencyFactor), 175, 201, 48);
    }
}

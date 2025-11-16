using System.Windows.Media;

public static class ColorConverterHelper
{
    /// <summary>
    /// converts a color RGB to HSV.
    /// </summary>
    /// <param name="color">RGB color to convert.</param>
    /// <returns>A tuple (H, S, V) where H is in [0, 360), S and V in [0, 1].</returns>
    public static (double H, double S, double V) RgbToHsv(System.Windows.Media.Color color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));

        double h, s, v = max;
        double diff = max - min;
        s = (max == 0) ? 0 : diff / max;

        if (max == min)
        {
            h = 0; // Grayscale, hue is undefined
        }
        else
        {
            if (max == r)
                h = (g - b) / diff + (g < b ? 6 : 0);
            else if (max == g)
                h = (b - r) / diff + 2;
            else // max == b
                h = (r - g) / diff + 4;
            h /= 6; // h in [0, 1)
        }

        return (h * 360.0, s, v); // h in [0, 360), s,v in [0, 1]
    }

    /// <summary>
    /// converts HSV to RGB.
    /// </summary>
    /// <param name="h">Hue in [0, 360).</param>
    /// <param name="s">Saturation in [0, 1].</param>
    /// <param name="v">Value/Brightness in [0, 1].</param>
    /// <returns>Il colore RGB corrispondente.</returns>
    public static System.Windows.Media.Color HsvToRgb(double h, double s, double v, byte alpha = 255)
    {
        double r = 0, g = 0, b = 0;

        if (s == 0)
        {
            // Grayscale
            r = g = b = v;
        }
        else
        {
            h /= 60.0; // h in [0, 6)
            int i = (int)Math.Floor(h);
            double f = h - i; // fractional part
            double p = v * (1 - s);
            double q = v * (1 - s * f);
            double t = v * (1 - s * (1 - f));

            switch (i)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                case 5: r = v; g = p; b = q; break;
            }
        }

        return System.Windows.Media.Color.FromArgb(alpha,
                              (byte)Math.Round(r * 255),
                              (byte)Math.Round(g * 255),
                              (byte)Math.Round(b * 255));
    }

    /// <summary>
    /// converts hexadecimal string (#AARRGGBB) to System.Windows.Media.Color.
    /// </summary>
    public static System.Windows.Media.Color ColorFromString(string hex)
    {
        try
        {
            return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return Colors.Black; // Fallback
        }
    }
}
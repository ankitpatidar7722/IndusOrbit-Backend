using System;

namespace Backend.Services;

public static class PasswordEncoder
{
    private static bool IsNumeric(string s)
    {
        return double.TryParse(s, out _);
    }

    private static double Val(string s)
    {
        // Simulate VB Val function (simple version for this context)
        // Here we expect numeric strings
        if (double.TryParse(s, out double d)) return d;
        return 0; // Fallback
    }

    private static int Asc(char c)
    {
        return (int)c;
    }

    public static string ChangePassword(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";

        string p = "", k = "";
        
        // Loop 1: Transform characters
        for (int i = 0; i < s.Length; i++)
        {
            k = s.Substring(i, 1);
            if (IsNumeric(k))
            {
                p += (Val(k) + 7).ToString();
            }
            else
            {
                p += (Asc(k[0]) + 7).ToString();
            }
        }

        s = "";
        
        // Loop 2: Process chunks of 4
        // The original VB loop iterates i from 1 to Len(p), but increments X by 4 inside.
        // It effectively processes chunks of 4.
        
        for (int x = 0; x < p.Length; x += 4)
        {
            int length = Math.Min(4, p.Length - x);
            k = p.Substring(x, length);
            
            if (!string.IsNullOrEmpty(k))
            {
                s += (Val(k) * 7).ToString();
            }
        }

        return s;
    }
}

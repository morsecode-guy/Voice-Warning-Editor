using System;
using System.IO;
using UnityEngine;
using Il2Cpp;

namespace VoiceWarningEditor
{
    // icon generation and helpers
    public partial class VoiceWarningEditorMod
    {
        // load vws_icon.png or generate a speaker icon :3
        private Sprite LoadOrCreateVwsIcon()
        {
            const int SIZE = 128;

            try
            {
                // try user-provided icon
                string iconPath = Path.Combine(_dataFolderPath, "vws_icon.png");
                if (File.Exists(iconPath))
                {
                    byte[] fileData = File.ReadAllBytes(iconPath);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (ImageConversion.LoadImage(tex, fileData))
                    {
                        tex.filterMode = FilterMode.Bilinear;
                        var sprite = Sprite.Create(tex,
                            new Rect(0, 0, tex.width, tex.height),
                            new Vector2(0.5f, 0.5f), 100f);
                        LoggerInstance.Msg($"[Icon] Loaded vws_icon.png ({tex.width}x{tex.height})");
                        return sprite;
                    }
                }

                // procedurally generate it
                var iconTex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false);
                var pixels = new Color[SIZE * SIZE];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = Color.clear;

                float cy = SIZE * 0.5f;

                for (int y = 0; y < SIZE; y++)
                {
                    for (int x = 0; x < SIZE; x++)
                    {
                        float px = x + 0.5f;
                        float py = y + 0.5f;
                        bool filled = false;

                        // speaker body
                        float bx0 = SIZE * 0.16f, bx1 = SIZE * 0.30f;
                        float by0 = SIZE * 0.34f, by1 = SIZE * 0.66f;
                        if (px >= bx0 && px <= bx1 && py >= by0 && py <= by1)
                            filled = true;

                        // speaker cone
                        float cx0 = SIZE * 0.24f, cx1 = SIZE * 0.44f;
                        if (px >= cx0 && px <= cx1)
                        {
                            float t = (px - cx0) / (cx1 - cx0);
                            float halfBody = (by1 - by0) * 0.5f;
                            float halfCone = SIZE * 0.38f;
                            float halfH = halfBody + t * (halfCone - halfBody);
                            if (py >= cy - halfH && py <= cy + halfH)
                                filled = true;
                        }

                        // sound wave arcs
                        float dx = px - SIZE * 0.44f;
                        float dy = py - cy;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);

                        if (dx > 0)
                        {
                            float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                            if (Mathf.Abs(angle) < 45f)
                            {
                                float r1 = SIZE * 0.18f;
                                if (Mathf.Abs(dist - r1) < SIZE * 0.022f)
                                    filled = true;

                                float r2 = SIZE * 0.30f;
                                if (Mathf.Abs(dist - r2) < SIZE * 0.024f)
                                    filled = true;

                                float r3 = SIZE * 0.42f;
                                if (Mathf.Abs(dist - r3) < SIZE * 0.026f)
                                    filled = true;
                            }
                        }

                        if (filled)
                            pixels[y * SIZE + x] = Color.white;
                    }
                }

                iconTex.SetPixels(pixels);
                iconTex.Apply();
                iconTex.filterMode = FilterMode.Bilinear;

                // save it so user can replace later
                try
                {
                    byte[] pngData = ImageConversion.EncodeToPNG(iconTex);
                    File.WriteAllBytes(Path.Combine(_dataFolderPath, "vws_icon.png"), pngData);
                    LoggerInstance.Msg("[Icon] Generated and saved vws_icon.png");
                }
                catch { }

                return Sprite.Create(iconTex,
                    new Rect(0, 0, SIZE, SIZE),
                    new Vector2(0.5f, 0.5f), 100f);
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[Icon] Failed to create icon: {ex.Message}");
                return null;
            }
        }

        // load folder_icon.png or generate a folder icon :3
        private Sprite LoadOrCreateFolderIcon()
        {
            try
            {
                string dataDir = Path.Combine(
                    Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "",
                    "..", "UserData", "VoiceWarningEditor");
                string iconPath = Path.Combine(dataDir, "folder_icon.png");

                if (File.Exists(iconPath))
                {
                    byte[] fileData = File.ReadAllBytes(iconPath);
                    var loadedTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (ImageConversion.LoadImage(loadedTex, fileData))
                    {
                        loadedTex.filterMode = FilterMode.Bilinear;
                        return Sprite.Create(loadedTex,
                            new Rect(0, 0, loadedTex.width, loadedTex.height),
                            new Vector2(0.5f, 0.5f), 100f);
                    }
                }

                const int SIZE = 128;
                var iconTex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false);
                Color clear = new Color(0, 0, 0, 0);
                Color white = Color.white;

                var pixels = new Color[SIZE * SIZE];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

                // folder body with rounded corners
                int bodyL = 10, bodyR = 118, bodyB = 10, bodyT = 80;
                int cornerR = 6;
                for (int y = bodyB; y <= bodyT; y++)
                {
                    for (int x = bodyL; x <= bodyR; x++)
                    {
                        bool inCorner = false;
                        int cx = 0, cy = 0;
                        if (x < bodyL + cornerR && y < bodyB + cornerR) { cx = bodyL + cornerR; cy = bodyB + cornerR; inCorner = true; }
                        else if (x > bodyR - cornerR && y < bodyB + cornerR) { cx = bodyR - cornerR; cy = bodyB + cornerR; inCorner = true; }
                        else if (x < bodyL + cornerR && y > bodyT - cornerR) { cx = bodyL + cornerR; cy = bodyT - cornerR; inCorner = true; }
                        else if (x > bodyR - cornerR && y > bodyT - cornerR) { cx = bodyR - cornerR; cy = bodyT - cornerR; inCorner = true; }

                        if (inCorner)
                        {
                            float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                            if (dist > cornerR) continue;
                        }
                        pixels[y * SIZE + x] = white;
                    }
                }

                // folder tab with rounded top
                int tabL = 10, tabR = 50, tabB = 80, tabT = 95;
                int tabCorner = 5;
                for (int y = tabB; y <= tabT; y++)
                {
                    for (int x = tabL; x <= tabR; x++)
                    {
                        bool inCorner = false;
                        int cx = 0, cy = 0;
                        if (x < tabL + tabCorner && y > tabT - tabCorner) { cx = tabL + tabCorner; cy = tabT - tabCorner; inCorner = true; }
                        else if (x > tabR - tabCorner && y > tabT - tabCorner) { cx = tabR - tabCorner; cy = tabT - tabCorner; inCorner = true; }

                        if (inCorner)
                        {
                            float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                            if (dist > tabCorner) continue;
                        }
                        pixels[y * SIZE + x] = white;
                    }
                }

                iconTex.SetPixels(pixels);
                iconTex.Apply();
                iconTex.filterMode = FilterMode.Bilinear;

                try
                {
                    if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
                    File.WriteAllBytes(iconPath, ImageConversion.EncodeToPNG(iconTex));
                }
                catch { }

                return Sprite.Create(iconTex,
                    new Rect(0, 0, SIZE, SIZE),
                    new Vector2(0.5f, 0.5f), 100f);
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[Icon] Failed to create folder icon: {ex.Message}");
                return null;
            }
        }

        // lowest fuel fraction across all fuel systems
        private float GetFuelFraction(Craft craft)
        {
            // try craft.resources.fuelSystems[].FuelFraction
            try
            {
                var resources = craft.resources;
                if (resources != null && resources.fuelSystems != null)
                {
                    int count = resources.fuelSystems.Count;
                    if (count > 0)
                    {
                        float lowestFraction = 1.0f;
                        for (int i = 0; i < count; i++)
                        {
                            var fs = resources.fuelSystems[i];
                            if (fs != null)
                            {
                                float frac = fs.FuelFraction;
                                if (frac < lowestFraction)
                                    lowestFraction = frac;
                            }
                        }
                        return lowestFraction;
                    }
                }
            }
            catch { }

            // fallback: check fuelMass
            try
            {
                float fuelMass = craft.fuelMass;
                if (fuelMass <= 0.01f)
                    return 0f;
            }
            catch { }

            // assume fine if we cant tell
            return 1.0f;
        }
    }
}

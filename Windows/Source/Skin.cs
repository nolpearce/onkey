using System;
using System.Drawing;

namespace OnkeyDesktopPet
{
    // A look Onkey can wear: a whole sprite the size of Onkey.png with his eyes, arms and hands in
    // the same places, so the rig, blinking and watching eyes work on every skin. The pictures
    // besides Onkey.png are drawn by Assets/Skins/make-skins.py. Mac/Sources/Skin.swift matches.
    public sealed class Skin
    {
        public string Id, Name, Caption;
        public string File;          // Under Assets.
        public Rectangle LidSample;  // Sprite pixels whose colour his eyelids take.
        public bool Mouth;           // Opens a mouth over his lips when he makes his sound.

        public static readonly Skin Classic = new Skin
        {
            Id = "classic", Name = "Classic", Caption = "good old Onkey", File = "Onkey.png",
            LidSample = new Rectangle(748, 405, 10, 10), Mouth = true
        };
        // A carved jack-o'-lantern head; his grin is carved, so no mouth opens over it.
        public static readonly Skin Pumpkin = new Skin
        {
            Id = "pumpkin", Name = "Pumpkin", Caption = "spooky season", File = @"Skins\Pumpkin.png",
            LidSample = new Rectangle(664, 528, 10, 10), Mouth = false
        };
        public static readonly Skin[] All = { Classic, Pumpkin };

        public static Skin Find(string id)
        {
            foreach (Skin skin in All) if (skin.Id == id) return skin;
            return Classic;
        }
    }
}

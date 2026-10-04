import CoreGraphics

// A look Onkey can wear: a whole sprite the size of Onkey.png with his eyes, arms and hands in
// the same places, so the rig, blinking and watching eyes work on every skin. The pictures
// besides Onkey.png are drawn by Assets/Skins/make-skins.py. Windows/Source/Skin.cs matches.
struct Skin {
    let id, name, caption: String
    let file: String           // Under Resources.
    let lidSample: CGRect      // Sprite pixels whose colour his eyelids take.
    let mouth: Bool            // Opens a mouth over his lips when he makes his sound.

    static let classic = Skin(id: "classic", name: "Classic", caption: "good old Onkey", file: "Onkey.png",
                              lidSample: CGRect(x: 748, y: 405, width: 10, height: 10), mouth: true)
    // A carved jack-o'-lantern head; his grin is carved, so no mouth opens over it.
    static let pumpkin = Skin(id: "pumpkin", name: "Pumpkin", caption: "spooky season", file: "Skins/Pumpkin.png",
                              lidSample: CGRect(x: 664, y: 528, width: 10, height: 10), mouth: false)
    static let all = [classic, pumpkin]

    static func find(_ id: String?) -> Skin { all.first { $0.id == id } ?? classic }
}

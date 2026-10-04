import CoreGraphics

// A look Onkey can wear: a whole sprite the size of Onkey.png with his eyes, arms and hands in
// the same places, so the rig, blinking and watching eyes work on every skin. The pictures
// besides Onkey.png are drawn by Assets/Skins/make-skins.py. Windows/Source/Skin.cs matches.
struct Skin {
    let id, name, caption: String
    let file: String           // Under Resources.
    let lidSample: CGRect      // Sprite pixels whose colour his eyelids take.
    let mouth: Bool            // Opens a mouth over his lips when he makes his sound.
    // Below his ears, his body ends here at each side and the rig's arms come out from under it.
    var bodyLeft: CGFloat = 606, bodyRight: CGFloat = 1150

    static let classic = Skin(id: "classic", name: "Classic", caption: "good old Onkey", file: "Onkey.png",
                              lidSample: CGRect(x: 748, y: 405, width: 10, height: 10), mouth: true)
    // A round carved jack-o'-lantern head, wider than his classic body; his grin is carved, so no
    // mouth opens over it, and his eyelids are the carving's brown.
    static let pumpkin = Skin(id: "pumpkin", name: "Pumpkin", caption: "spooky season", file: "Skins/Pumpkin.png",
                              lidSample: CGRect(x: 650, y: 530, width: 10, height: 10), mouth: false,
                              bodyLeft: 470, bodyRight: 1330)
    static let all = [classic, pumpkin]

    static func find(_ id: String?) -> Skin { all.first { $0.id == id } ?? classic }
}

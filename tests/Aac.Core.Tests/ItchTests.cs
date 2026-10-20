using Aac.Core.Board;
using Aac.Core.Input;
using Aac.Core.Itch;
using Aac.Core.Session;

namespace Aac.Core.Tests;

public class ItchTests
{
    /// <summary>A click on the body model: the region is whichever is nearest the point clicked.</summary>
    private static string Where(double x, double y, double z)
    {
        var p = new Vec(x, y, z);
        return ItchDescriber.Where(Mannequin.Nearest(p), p);
    }

    [Theory]
    // Points on the realistic body's surface. It faces +z; the user's left is +x; the arms angle out, palms in.
    [InlineData(0.257, 1.250, -0.096, "on the back of my left upper arm, in the middle")]
    [InlineData(-0.243, 1.334, 0.0, "on the front of my right upper arm, near the shoulder")]
    [InlineData(0.184, 1.317, 0.024, "in my left armpit")]
    [InlineData(0.286, 1.129, -0.014, "on the inside of my left elbow")]
    [InlineData(0.363, 0.919, 0.029, "on the inside of my left wrist")]
    [InlineData(0.072, 1.627, -0.026, "behind my left ear")]
    [InlineData(-0.077, 1.688, 0.005, "above my right ear")]
    [InlineData(0.007, 1.638, 0.109, "between my eyes")]
    [InlineData(0.0, 1.6, 0.14, "on my nose")]
    [InlineData(0.015, 1.333, -0.125, "on my spine, between my shoulder blades")]
    [InlineData(0.107, 1.336, -0.134, "on my left shoulder blade")]
    [InlineData(-0.05, 1.05, -0.11, "on my lower back, on the right")]
    [InlineData(0.068, 0.896, -0.117, "on my left buttock")]
    [InlineData(0.389, 0.867, 0.064, "on the palm of my left hand")]
    [InlineData(0.422, 0.865, 0.059, "on the back of my left hand")]
    [InlineData(0.380, 0.868, 0.117, "on my left thumb")]
    [InlineData(0.433, 0.815, 0.120, "on my left index finger")]
    [InlineData(0.441, 0.756, 0.079, "on the tip of my left ring finger")]
    [InlineData(-0.431, 0.808, 0.031, "on my right pinky")]
    [InlineData(0.040, 0.684, -0.028, "on my left inner thigh, in the middle")]
    [InlineData(0.189, 0.690, -0.024, "on the outside of my left thigh, in the middle")]
    [InlineData(0.140, 0.487, 0.007, "on my left kneecap")]
    [InlineData(0.139, 0.483, -0.101, "on the back of my left knee")]
    [InlineData(-0.163, 0.298, -0.137, "on my right calf, in the middle")]
    [InlineData(0.151, 0.097, -0.091, "on the inside of my left ankle")]
    [InlineData(0.217, 0.025, 0.074, "on my left big toe")]
    [InlineData(0.185, 0.019, -0.120, "on my left heel")]
    public void A_clicked_point_becomes_words_a_carer_can_follow(double x, double y, double z, string expected) =>
        Assert.Equal(expected, Where(x, y, z));

    [Fact]
    public void Every_part_can_be_described_and_is_found_from_its_own_centre()
    {
        foreach (var part in Mannequin.Parts)
        {
            Assert.StartsWith("I have an itch ", ItchDescriber.Sentence(part, part.A));
            var middle = part.A + (part.B - part.A) * 0.5;
            Assert.Equal(part.Id, Mannequin.Nearest(middle).Id);
        }
    }

    [Fact]
    public void Itch_opens_beside_the_keyboard_a_click_types_the_sentence_and_the_words_are_spoken()
    {
        var rig = new Rig();
        Assert.Equal(CommandId.Itch, rig.Session.Rows[0].Cells[^1].Cell.Command); // top right
        rig.Clock.AdvanceMs(1000);
        rig.Session.ActivateCell("itch");
        Assert.True(rig.Session.ItchOpen);
        Assert.Contains(rig.Session.Rows, r => r.Name == "keys-0"); // the keyboard stays
        Assert.Equal(CommandId.ItchTurnRight, rig.Session.FocusedCell?.Command); // ready to turn the figure

        // The user clicks the spot on the figure itself.
        rig.Session.MarkItch("upperarm-l", new Vec(0.257, 1.250, -0.096));
        Assert.Equal("I have an itch on the back of my left upper arm, in the middle.", rig.Session.Draft);
        Assert.Empty(rig.Engine.Spoken); // typed out, not said until Say it

        // The arrows say the words, to guide the person scratching.
        foreach (var word in new[] { "Up", "Left" })
        {
            rig.Clock.AdvanceMs(3000);
            rig.Session.ActivateCell($"dir-{word}");
            rig.Engine.FinishLast();
        }
        Assert.Equal(new[] { "Up", "Left" }, rig.Engine.Spoken.Select(u => u.DisplayText));

        rig.Clock.AdvanceMs(1000);
        rig.Session.ActivateCell("itch"); // the Itch button closes it again
        Assert.False(rig.Session.ItchOpen);
        Assert.DoesNotContain(rig.Session.Rows, r => r.Name.StartsWith(CommunicationSession.ItchRowPrefix));
    }

    [Fact]
    public void Turning_tilting_and_zooming_stay_in_range()
    {
        var rig = new Rig();
        rig.Session.OpenItch(true);
        for (var i = 0; i < 9; i++) { rig.Clock.AdvanceMs(1000); rig.Session.ActivateCell("itch-turn-right"); }
        Assert.Equal(45, rig.Session.ItchYaw, 6);
        rig.Session.TurnItch(-90);
        Assert.Equal(315, rig.Session.ItchYaw, 6);
        for (var i = 0; i < 9; i++) { rig.Clock.AdvanceMs(1000); rig.Session.ActivateCell("itch-zoom-in"); }
        Assert.Equal(CommunicationSession.ItchMaxZoom, rig.Session.ItchZoom);
        rig.Session.TiltItch(500);
        Assert.Equal(CommunicationSession.ItchMaxPitch, rig.Session.ItchPitch);
        rig.Clock.AdvanceMs(1000);
        rig.Session.ActivateCell("itch-back");
        Assert.Equal(180, rig.Session.ItchYaw);
        Assert.Equal(0, rig.Session.ItchPitch); // Front and Back also level the view

        // Programmable keys (and the mouse wheel) turn and zoom the doll while Itch is open.
        rig.Session.HandleAction(InputAction.DollTurnRight);
        Assert.Equal(225, rig.Session.ItchYaw, 6);
        rig.Session.HandleAction(InputAction.DollFarther);
        Assert.True(rig.Session.ItchZoom < CommunicationSession.ItchMaxZoom);
        rig.Session.ZoomItch(-40);
        Assert.Equal(1, rig.Session.ItchZoom);
        rig.Session.OpenItch(false);
        rig.Session.HandleAction(InputAction.DollTurnRight); // ignored with Itch closed
        Assert.Equal(225, rig.Session.ItchYaw, 6);
    }

    [Fact]
    public void Moving_the_view_never_leaves_the_body_and_Centre_brings_it_back()
    {
        var rig = new Rig();
        double OffBody() => Mannequin.Distance(Mannequin.Nearest(rig.Session.ItchTarget), rig.Session.ItchTarget);
        rig.Session.OpenItch(true);
        for (var i = 0; i < 50; i++) rig.Session.HandleAction(InputAction.DollMoveRight);
        for (var i = 0; i < 50; i++) rig.Session.HandleAction(InputAction.DollMoveUp);
        // Stopped near the body, not off screen.
        Assert.True(OffBody() <= rig.Session.ItchLeash + 1e-9, $"{OffBody()} from the body");
        rig.Session.PanItch(-100, -100);
        Assert.True(OffBody() <= rig.Session.ItchLeash + 1e-9);
        for (var i = 0; i < 12; i++) rig.Session.HandleAction(InputAction.DollCloser);
        for (var i = 0; i < 50; i++) rig.Session.HandleAction(InputAction.DollMoveLeft);
        Assert.True(OffBody() <= 0.025 + 1e-9); // zoomed right in, the view stays within 2.5 cm of the skin
        rig.Session.ZoomItch(-40);

        // Moving is gentler when zoomed in: a key press moves a fifth of what is on screen.
        rig.Session.HandleAction(InputAction.DollCloser);
        rig.Session.HandleAction(InputAction.DollCloser);
        var before = rig.Session.ItchTarget;
        rig.Session.HandleAction(InputAction.DollMoveUp);
        Assert.Equal(0.4 / rig.Session.ItchZoom, rig.Session.ItchTarget.Y - before.Y, 6);

        rig.Clock.AdvanceMs(1000);
        rig.Session.ActivateCell("itch-centre");
        Assert.Equal(CommunicationSession.ItchHome, rig.Session.ItchTarget);
        Assert.Equal(1, rig.Session.ItchZoom);
    }

    [Fact]
    public void Zooming_toward_a_point_keeps_that_point_in_place_on_screen()
    {
        var rig = new Rig();
        rig.Session.OpenItch(true);
        var hand = new Vec(0.41, 0.85, 0.07);
        // Seen from the front, a point's place on screen is its offset from the centre times the zoom.
        double ScreenX() => (hand.X - rig.Session.ItchTarget.X) * rig.Session.ItchZoom;
        double ScreenY() => (hand.Y - rig.Session.ItchTarget.Y) * rig.Session.ItchZoom;
        var (x0, y0) = (ScreenX(), ScreenY());
        for (var i = 0; i < 4; i++)
        {
            rig.Session.ZoomItch(1, hand);
            Assert.Equal(x0, ScreenX(), 6);
            Assert.Equal(y0, ScreenY(), 6);
        }
    }

    [Fact]
    public void Panel_buttons_can_be_closed_and_reopened_straight_away_but_a_bounce_is_still_ignored()
    {
        var rig = new Rig();
        rig.Session.ActivateCell("itch");
        Assert.True(rig.Session.ItchOpen);
        rig.Clock.AdvanceMs(30);
        rig.Session.ActivateCell("itch"); // a switch bounce
        Assert.True(rig.Session.ItchOpen);
        rig.Clock.AdvanceMs(150);
        rig.Session.ActivateCell("itch"); // close
        Assert.False(rig.Session.ItchOpen);
        rig.Clock.AdvanceMs(150);
        rig.Session.ActivateCell("itch"); // and open again at once
        Assert.True(rig.Session.ItchOpen);
        rig.Clock.AdvanceMs(150);
        rig.Session.ActivateCell("go-quick");
        rig.Clock.AdvanceMs(150);
        rig.Session.ActivateCell("go-quick");
        Assert.Equal(Drawer.None, rig.Session.Drawer);
    }
}

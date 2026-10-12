namespace Soccer.Core
{
    public struct GameplayButtons
    {
        public bool OnField, MovementOnly, Cancel, KickDown, KickHeld, KickUp, PushDown, QDown, EDown, LeftDown, SpecialDown;
    }
    /// <summary>Independent gestures: keyboard kick, keyboard push, mouse confirmation of a selected skill.</summary>
    public sealed class GameplayGesture
    {
        private bool kicking;
        public bool Targeting { get; private set; }
        public void Reset() { kicking = Targeting = false; }
        public void Apply(ref PlayerCommand command, CharacterId character, GameplayButtons buttons)
        {
            if (buttons.Cancel) { command.Cancel = true; Reset(); command.KickHeld = false; return; }
            if (buttons.MovementOnly) { Reset(); command.KickHeld = false; return; }
            if (buttons.OnField && buttons.KickDown) { Targeting = false; kicking = true; command.KickPressed = true; }
            command.KickHeld = kicking && buttons.KickHeld;
            if (kicking && buttons.KickUp)
            { command.KickReleased |= buttons.OnField; command.Cancel |= !buttons.OnField; kicking = false; }
            if (!buttons.OnField) return;
            command.TacklePressed |= buttons.PushDown; command.QPressed |= buttons.QDown; command.SpecialPressed |= buttons.SpecialDown;
            if (buttons.EDown)
            {
                if (character == CharacterId.Thor || character == CharacterId.WindShuttle)
                { Targeting = !Targeting; if (character == CharacterId.WindShuttle) { command.EPressed |= Targeting; command.Cancel |= !Targeting; } }
                else command.EPressed = true;
            }
            if (Targeting && buttons.LeftDown)
            {
                if (character == CharacterId.Thor) command.EPressed = true;
                else command.EReleased = true;
                Targeting = false;
            }
        }
    }
}

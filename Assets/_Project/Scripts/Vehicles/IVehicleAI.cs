namespace GTH6
{
    /// <summary>
    /// An AI driver (traffic, police). The player can take a car from one: PlayerVehicleInteraction
    /// calls Eject, the AI stops driving, and the player takes the seat.
    /// </summary>
    public interface IVehicleAI
    {
        void Eject();
    }
}

namespace ComelitApiGateway.Commons.Enums.Vedo
{
    /// <summary>
    /// Identifies the VEDO firmware dialect used to arm/disarm the alarm.
    /// </summary>
    public enum VedoFirmwareVersion
    {
        /// <summary>
        /// Older firmware (e.g. 2.7.X): arm/disarm via HTTP GET on /action.cgi.
        /// </summary>
        V1,
        /// <summary>
        /// Newer firmware (e.g. 2.15.X): arm/disarm via HTTP POST on /action.cgi.
        /// </summary>
        V2
    }
}

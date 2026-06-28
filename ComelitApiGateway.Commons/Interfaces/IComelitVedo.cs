using ComelitApiGateway.Commons.Dtos.Vedo;
using ComelitApiGateway.Commons.Dtos.Vedo.ComelitSystem;

namespace ComelitApiGateway.Commons.Interfaces
{
    public interface IComelitVedo
    {

        #region Vedo System Api Call

        /// <summary>
        /// Login and get Cookie UID
        /// </summary>
        /// <returns>Cookie UID</returns>
        Task<string> LoginAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Get decriptions of all Areas
        /// </summary>
        /// <returns></returns>
        Task<AreaDescriptionResponseDTO> ComelitGetAreasDescriptionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Get status of all the Areas
        /// </summary>
        /// <returns></returns>
        Task<AreaStatusResponseDTO> ComelitGetAreasStatusAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Get status of all the Zones of each Areas
        /// </summary>
        /// <returns></returns>
        Task<ZoneStatusResponseDTO> ComelitGetZonesStatusAsync(CancellationToken cancellationToken = default);

        #endregion

        #region Api call wrapper

        /// <summary>
        /// Get list of Areas with their ID
        /// </summary>
        /// <returns></returns>
        Task<List<VedoAreaDTO>> GetAreasListAsync(CancellationToken cancellationToken = default);
        Task<List<VedoAreaStatusDTO>> GetAreasStatusAsync(CancellationToken cancellationToken = default);
        Task<List<VedoZoneDTO>> GetZoneListAsync(int idArea = 0, bool removeHiddenZones = true, CancellationToken cancellationToken = default);

        Task<bool> ArmAlarmAsync(int? area = null, bool force = true, CancellationToken cancellationToken = default);
        Task<bool> DisarmAlarmAsync(int? area = null, bool force = true, CancellationToken cancellationToken = default);
        Task<bool> ExcludeZoneAsync(int zoneId, CancellationToken cancellationToken = default);
        Task<bool> IncludeZoneAsync(int zoneId, CancellationToken cancellationToken = default);
        Task<bool> IsolateZoneAsync(int zoneId, CancellationToken cancellationToken = default);
        Task<bool> UnisolateZoneAsync(int zoneId, CancellationToken cancellationToken = default);
        #endregion
    }
}

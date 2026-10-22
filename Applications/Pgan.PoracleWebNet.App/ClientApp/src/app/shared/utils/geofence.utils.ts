/** Status-to-color mapping for geofence polygons and UI elements. */
export const GEOFENCE_STATUS_COLORS: Record<string, string> = {
  active: '#2196f3',
  approved: '#4caf50',
  pending_review: '#ff9800',
  rejected: '#f44336',
};

/**
 * The one rule for a geofence name: create, rename, import and the promoted name an admin approves. It is
 * `UserGeofenceService.MyRegex` on the server, applied there to the trimmed name, byte for byte. Letters are
 * A-Z only, so an accented name is refused on both sides alike.
 */
export const GEOFENCE_NAME_PATTERN = /^[a-zA-Z0-9 \-'.()&]+$/;

// ─── Monster ───────────────────────────────────────────────────────────────────

export interface Monster {
  atk: number;
  clean: number;
  def: number;
  distance: number;
  form: number;
  gender: number;
  id: string;
  maxAtk: number;
  maxCp: number;
  maxDef: number;
  maxIv: number;
  maxLevel: number;
  maxSta: number;
  maxWeight: number;
  minCp: number;
  minIv: number;
  minLevel: number;
  minWeight: number;
  ping: string | null;
  pokemonId: number;
  profileNo: number;
  pvpRankingBest: number;
  pvpRankingLeague: number;
  pvpRankingMinCp: number;
  pvpRankingWorst: number;
  sta: number;
  template: string | null;
  uid: number;
}

export type MonsterCreate = Omit<Monster, 'uid' | 'id' | 'profileNo'>;

export type MonsterUpdate = Partial<MonsterCreate>;

// ─── Raid ──────────────────────────────────────────────────────────────────────

export interface Raid {
  clean: number;
  distance: number;
  exclusive: number;
  form: number;
  gymId: string | null;
  id: string;
  level: number;
  move: number;
  ping: string | null;
  pokemonId: number;
  profileNo: number;
  team: number;
  template: string | null;
  uid: number;
}

export type RaidCreate = Omit<Raid, 'uid' | 'id' | 'profileNo'>;

export type RaidUpdate = Partial<RaidCreate>;

// ─── Egg ───────────────────────────────────────────────────────────────────────

export interface Egg {
  clean: number;
  distance: number;
  exclusive: number;
  id: string;
  level: number;
  ping: string | null;
  profileNo: number;
  team: number;
  template: string | null;
  uid: number;
}

export type EggCreate = Omit<Egg, 'uid' | 'id' | 'profileNo'>;

export type EggUpdate = Partial<EggCreate>;

// ─── Quest ─────────────────────────────────────────────────────────────────────

export interface Quest {
  clean: number;
  distance: number;
  id: string;
  ping: string | null;
  pokemonId: number;
  profileNo: number;
  reward: number;
  rewardType: number;
  shiny: number;
  template: string | null;
  uid: number;
}

export type QuestCreate = Omit<Quest, 'uid' | 'id' | 'profileNo'>;

export type QuestUpdate = Partial<QuestCreate>;

// ─── Invasion ──────────────────────────────────────────────────────────────────

export interface Invasion {
  clean: number;
  distance: number;
  gender: number;
  gruntType: string | null;
  id: string;
  ping: string | null;
  profileNo: number;
  template: string | null;
  uid: number;
}

export type InvasionCreate = Omit<Invasion, 'uid' | 'id' | 'profileNo'>;

export type InvasionUpdate = Partial<InvasionCreate>;

// ─── Lure ──────────────────────────────────────────────────────────────────────

export interface Lure {
  clean: number;
  distance: number;
  id: string;
  lureId: number;
  ping: string | null;
  profileNo: number;
  template: string | null;
  uid: number;
}

export type LureCreate = Omit<Lure, 'uid' | 'id' | 'profileNo'>;

export type LureUpdate = Partial<LureCreate>;

// ─── Nest ──────────────────────────────────────────────────────────────────────

export interface Nest {
  clean: number;
  distance: number;
  id: string;
  minSpawnAvg: number;
  ping: string | null;
  pokemonId: number;
  profileNo: number;
  template: string | null;
  uid: number;
}

export type NestCreate = Omit<Nest, 'uid' | 'id' | 'profileNo'>;

export type NestUpdate = Partial<NestCreate>;

// ─── Gym ───────────────────────────────────────────────────────────────────────

export interface Gym {
  clean: number;
  distance: number;
  gymId: string | null;
  id: string;
  ping: string | null;
  profileNo: number;
  slotChanges: number;
  team: number;
  template: string | null;
  uid: number;
}

export type GymCreate = Omit<Gym, 'uid' | 'id' | 'profileNo'>;

export type GymUpdate = Partial<GymCreate>;

// ─── Human / User ──────────────────────────────────────────────────────────────

export interface Human {
  adminDisable: number;
  area: string;
  communityMembership: string | null;
  enabled: number;
  id: string;
  language: string;
  latitude: number;
  longitude: number;
  name: string;
}

/** Shape returned by GET /api/admin/users (anonymous projection from AdminController). */
export interface AdminUser {
  adminDisable: number;
  avatarUrl: string | null;
  currentProfileNo: number;
  disabledDate: string | null;
  enabled: number;
  id: string;
  language: string | null;
  lastChecked: string | null;
  name: string | null;
  type: string | null;
}

// ─── Profile ───────────────────────────────────────────────────────────────────

export interface Profile {
  active: boolean;
  name: string;
  profileNo: number;
}

export interface ProfileCreate {
  name: string;
}

// ─── Dashboard ─────────────────────────────────────────────────────────────────

export interface DashboardCounts {
  eggs: number;
  gyms: number;
  invasions: number;
  lures: number;
  nests: number;
  pokemon: number;
  quests: number;
  raids: number;
}

// ─── Auth / User Info ──────────────────────────────────────────────────────────

export interface UserInfo {
  avatarUrl: string | null;
  enabled: boolean;
  id: string;
  isAdmin: boolean;
  managedWebhooks?: string[] | null;
  profileName: string | null;
  profileNo: number;
  type: string;
  username: string;
}

export interface LoginResponse {
  token: string;
  user: UserInfo;
}

export interface TelegramConfig {
  botUsername: string;
  enabled: boolean;
}

// ─── Poracle Config ────────────────────────────────────────────────────────────

export interface PoracleConfig {
  areas: AreaDefinition[];
  forms: Record<string, string>;
  grunts: Record<string, string>;
  items: Record<string, string>;
  moves: Record<string, string>;
  pokemon: Record<string, string>;
}

export interface AreaDefinition {
  description?: string;
  group: string;
  name: string;
  userSelectable: boolean;
}

// ─── Location ──────────────────────────────────────────────────────────────────

export interface Location {
  latitude: number;
  longitude: number;
  name?: string;
}

// ─── Geocoding ────────────────────────────────────────────────────────────────

export interface GeocodingAddress {
  city?: string;
  country?: string;
  house_number?: string;
  postcode?: string;
  road?: string;
  state?: string;
  town?: string;
  village?: string;
}

export interface GeocodingResult {
  address?: GeocodingAddress;
  display_name: string;
  lat: string;
  lon: string;
}

export interface ReverseGeocodingResult {
  address?: GeocodingAddress;
  display_name: string;
}

// ─── Geofence ─────────────────────────────────────────────────────────────────

export interface GeofenceData {
  id: number;
  name: string;
  path: [number, number][];
}

// ─── Area ──────────────────────────────────────────────────────────────────────

export interface AreaSelection {
  group: string;
  name: string;
  selected: boolean;
}

// ─── PwebSetting ──────────────────────────────────────────────────────────────

export interface PwebSetting {
  setting: string;
  value: string | null;
}

// ─── User Geofence ───────────────────────────────────────────────────────────

export interface UserGeofence {
  createdAt: string;
  displayName: string;
  groupName: string;
  humanId: string;
  id: number;
  kojiName: string;
  parentId: number;
  polygon?: [number, number][];
  promotedName?: string;
  reviewedAt?: string;
  reviewedBy?: string;
  reviewNotes?: string;
  status: 'active' | 'pending_review' | 'approved' | 'rejected';
  submittedAt?: string;
  updatedAt: string;
}

export interface UserGeofenceCreate {
  displayName: string;
  groupName: string;
  parentId: number;
  polygon: [number, number][]; // [lat, lng] pairs
}

export interface GeofenceRegion {
  displayName: string;
  id: number;
  name: string;
  polygon?: [number, number][];
}

// ─── Quick Picks ──────────────────────────────────────────────────────────────

export interface QuickPickDefinition {
  alarmType: string;
  category: string;
  description: string;
  enabled: boolean;
  filters: Record<string, unknown>;
  icon: string;
  id: string;
  name: string;
  scope: string;
  sortOrder: number;
}

export interface QuickPickAppliedState {
  appliedAt: string;
  excludePokemonIds: number[];
  quickPickId: string;
  trackedUids: number[];
}

export interface QuickPickApplyRequest {
  clean?: number;
  distance?: number;
  excludePokemonIds?: number[];
  template?: string;
}

export interface QuickPickSummary {
  appliedState: QuickPickAppliedState | null;
  definition: QuickPickDefinition;
}

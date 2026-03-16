// ─── Monster ───────────────────────────────────────────────────────────────────

export interface Monster {
  uid: number;
  id: string;
  pokemonId: number;
  ping: string | null;
  distance: number;
  minIv: number;
  maxIv: number;
  minCp: number;
  maxCp: number;
  minLevel: number;
  maxLevel: number;
  minWeight: number;
  maxWeight: number;
  atk: number;
  def: number;
  sta: number;
  maxAtk: number;
  maxDef: number;
  maxSta: number;
  pvpRankingWorst: number;
  pvpRankingBest: number;
  pvpRankingMinCp: number;
  pvpRankingLeague: number;
  form: number;
  gender: number;
  clean: number;
  template: string | null;
  profileNo: number;
}

export type MonsterCreate = Omit<Monster, 'uid' | 'id'>;

export type MonsterUpdate = Partial<MonsterCreate>;

// ─── Raid ──────────────────────────────────────────────────────────────────────

export interface Raid {
  uid: number;
  id: string;
  pokemonId: number;
  ping: string | null;
  distance: number;
  team: number;
  exclusive: number;
  form: number;
  move: number;
  level: number;
  clean: number;
  template: string | null;
  profileNo: number;
  gymId: string | null;
}

export type RaidCreate = Omit<Raid, 'uid' | 'id'>;

export type RaidUpdate = Partial<RaidCreate>;

// ─── Egg ───────────────────────────────────────────────────────────────────────

export interface Egg {
  uid: number;
  id: string;
  ping: string | null;
  distance: number;
  team: number;
  exclusive: number;
  level: number;
  clean: number;
  template: string | null;
  profileNo: number;
}

export type EggCreate = Omit<Egg, 'uid' | 'id'>;

export type EggUpdate = Partial<EggCreate>;

// ─── Quest ─────────────────────────────────────────────────────────────────────

export interface Quest {
  uid: number;
  id: string;
  pokemonId: number;
  ping: string | null;
  distance: number;
  clean: number;
  template: string | null;
  rewardType: number;
  reward: number;
  shiny: number;
  profileNo: number;
}

export type QuestCreate = Omit<Quest, 'uid' | 'id'>;

export type QuestUpdate = Partial<QuestCreate>;

// ─── Invasion ──────────────────────────────────────────────────────────────────

export interface Invasion {
  uid: number;
  id: string;
  ping: string | null;
  distance: number;
  clean: number;
  template: string | null;
  gruntType: string | null;
  gender: number;
  profileNo: number;
}

export type InvasionCreate = Omit<Invasion, 'uid' | 'id'>;

export type InvasionUpdate = Partial<InvasionCreate>;

// ─── Lure ──────────────────────────────────────────────────────────────────────

export interface Lure {
  uid: number;
  id: string;
  ping: string | null;
  distance: number;
  clean: number;
  template: string | null;
  lureId: number;
  profileNo: number;
}

export type LureCreate = Omit<Lure, 'uid' | 'id'>;

export type LureUpdate = Partial<LureCreate>;

// ─── Nest ──────────────────────────────────────────────────────────────────────

export interface Nest {
  uid: number;
  id: string;
  pokemonId: number;
  ping: string | null;
  distance: number;
  clean: number;
  template: string | null;
  minSpawnAvg: number;
  profileNo: number;
}

export type NestCreate = Omit<Nest, 'uid' | 'id'>;

export type NestUpdate = Partial<NestCreate>;

// ─── Gym ───────────────────────────────────────────────────────────────────────

export interface Gym {
  uid: number;
  id: string;
  ping: string | null;
  distance: number;
  clean: number;
  template: string | null;
  team: number;
  slot_changes: number;
  battle_changes: number;
  gymId: string | null;
  profileNo: number;
}

export type GymCreate = Omit<Gym, 'uid' | 'id'>;

export type GymUpdate = Partial<GymCreate>;

// ─── Human / User ──────────────────────────────────────────────────────────────

export interface Human {
  id: string;
  name: string;
  area: string;
  latitude: number;
  longitude: number;
  enabled: number;
  language: string;
  communityMembership: string | null;
}

/** Shape returned by GET /api/admin/users (anonymous projection from AdminController). */
export interface AdminUser {
  id: string;
  name: string | null;
  type: string | null;
  enabled: number;
  currentProfileNo: number;
  language: string | null;
  avatarUrl: string | null;
}

// ─── Profile ───────────────────────────────────────────────────────────────────

export interface Profile {
  profileNo: number;
  name: string;
  active: boolean;
}

export interface ProfileCreate {
  name: string;
}

// ─── Dashboard ─────────────────────────────────────────────────────────────────

export interface DashboardCounts {
  pokemon: number;
  raids: number;
  eggs: number;
  quests: number;
  invasions: number;
  lures: number;
  nests: number;
  gyms: number;
}

// ─── Auth / User Info ──────────────────────────────────────────────────────────

export interface UserInfo {
  id: string;
  username: string;
  type: string;
  isAdmin: boolean;
  profileNo: number;
  profileName: string | null;
  avatarUrl: string | null;
}

export interface LoginResponse {
  token: string;
  user: UserInfo;
}

export interface TelegramConfig {
  enabled: boolean;
  botUsername: string;
}

// ─── Poracle Config ────────────────────────────────────────────────────────────

export interface PoracleConfig {
  pokemon: Record<string, string>;
  forms: Record<string, string>;
  moves: Record<string, string>;
  items: Record<string, string>;
  grunts: Record<string, string>;
  areas: AreaDefinition[];
}

export interface AreaDefinition {
  name: string;
  group: string;
  userSelectable: boolean;
  description?: string;
}

// ─── Location ──────────────────────────────────────────────────────────────────

export interface Location {
  latitude: number;
  longitude: number;
  name?: string;
}

// ─── Geocoding ────────────────────────────────────────────────────────────────

export interface GeocodingAddress {
  road?: string;
  house_number?: string;
  city?: string;
  town?: string;
  village?: string;
  state?: string;
  country?: string;
  postcode?: string;
}

export interface GeocodingResult {
  lat: string;
  lon: string;
  display_name: string;
  address?: GeocodingAddress;
}

export interface ReverseGeocodingResult {
  display_name: string;
  address?: GeocodingAddress;
}

// ─── Geofence ─────────────────────────────────────────────────────────────────

export interface GeofenceData {
  id: number;
  name: string;
  path: [number, number][];
}

// ─── Area ──────────────────────────────────────────────────────────────────────

export interface AreaSelection {
  name: string;
  selected: boolean;
  group: string;
}

// ─── PwebSetting ──────────────────────────────────────────────────────────────

export interface PwebSetting {
  setting: string;
  value: string | null;
}

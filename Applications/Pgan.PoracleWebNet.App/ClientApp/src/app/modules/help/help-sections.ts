export interface HelpSection {
  /** Shown only to admins; everyone else neither sees the section nor finds it by search. */
  adminOnly?: boolean;
  contentKey: string;
  icon: string;
  id: string;
  subtitleKey: string;
  titleKey: string;
}

export const HELP_SECTIONS: HelpSection[] = [
  {
    id: 'getting-started',
    contentKey: 'HELP.CONTENT_GETTING_STARTED',
    icon: 'rocket_launch',
    subtitleKey: 'HELP.SECTION_GETTING_STARTED_SUB',
    titleKey: 'HELP.SECTION_GETTING_STARTED',
  },
  {
    id: 'dashboard',
    contentKey: 'HELP.CONTENT_DASHBOARD',
    icon: 'dashboard',
    subtitleKey: 'HELP.SECTION_DASHBOARD_SUB',
    titleKey: 'HELP.SECTION_DASHBOARD',
  },
  {
    id: 'location',
    contentKey: 'HELP.CONTENT_LOCATION',
    icon: 'my_location',
    subtitleKey: 'HELP.SECTION_LOCATION_SUB',
    titleKey: 'HELP.SECTION_LOCATION',
  },
  {
    id: 'areas',
    contentKey: 'HELP.CONTENT_AREAS',
    icon: 'map',
    subtitleKey: 'HELP.SECTION_AREAS_SUB',
    titleKey: 'HELP.SECTION_AREAS',
  },
  {
    id: 'geofences',
    contentKey: 'HELP.CONTENT_GEOFENCES',
    icon: 'draw',
    subtitleKey: 'HELP.SECTION_GEOFENCES_SUB',
    titleKey: 'HELP.SECTION_GEOFENCES',
  },
  {
    id: 'pokemon',
    contentKey: 'HELP.CONTENT_POKEMON',
    icon: 'catching_pokemon',
    subtitleKey: 'HELP.SECTION_POKEMON_SUB',
    titleKey: 'HELP.SECTION_POKEMON',
  },
  {
    id: 'other-alarms',
    contentKey: 'HELP.CONTENT_OTHER_ALARMS',
    icon: 'shield',
    subtitleKey: 'HELP.SECTION_OTHER_ALARMS_SUB',
    titleKey: 'HELP.SECTION_OTHER_ALARMS',
  },
  {
    id: 'delivery',
    contentKey: 'HELP.CONTENT_DELIVERY',
    icon: 'tune',
    subtitleKey: 'HELP.SECTION_DELIVERY_SUB',
    titleKey: 'HELP.SECTION_DELIVERY',
  },
  {
    id: 'quest-summary',
    contentKey: 'HELP.CONTENT_QUEST_SUMMARY',
    icon: 'schedule_send',
    subtitleKey: 'HELP.SECTION_QUEST_SUMMARY_SUB',
    titleKey: 'HELP.SECTION_QUEST_SUMMARY',
  },
  {
    id: 'test-alerts',
    contentKey: 'HELP.CONTENT_TEST_ALERTS',
    icon: 'send',
    subtitleKey: 'HELP.SECTION_TEST_ALERTS_SUB',
    titleKey: 'HELP.SECTION_TEST_ALERTS',
  },
  {
    id: 'pokemon-availability',
    contentKey: 'HELP.CONTENT_POKEMON_AVAILABILITY',
    icon: 'visibility',
    subtitleKey: 'HELP.SECTION_POKEMON_AVAILABILITY_SUB',
    titleKey: 'HELP.SECTION_POKEMON_AVAILABILITY',
  },
  {
    id: 'bulk',
    contentKey: 'HELP.CONTENT_BULK',
    icon: 'checklist',
    subtitleKey: 'HELP.SECTION_BULK_SUB',
    titleKey: 'HELP.SECTION_BULK',
  },
  {
    id: 'quick-picks',
    contentKey: 'HELP.CONTENT_QUICK_PICKS',
    icon: 'bolt',
    subtitleKey: 'HELP.SECTION_QUICK_PICKS_SUB',
    titleKey: 'HELP.SECTION_QUICK_PICKS',
  },
  {
    id: 'profiles',
    contentKey: 'HELP.CONTENT_PROFILES',
    icon: 'compare_arrows',
    subtitleKey: 'HELP.SECTION_PROFILES_SUB',
    titleKey: 'HELP.SECTION_PROFILES',
  },
  {
    id: 'cleaning',
    contentKey: 'HELP.CONTENT_CLEANING',
    icon: 'cleaning_services',
    subtitleKey: 'HELP.SECTION_CLEANING_SUB',
    titleKey: 'HELP.SECTION_CLEANING',
  },
  {
    id: 'appearance',
    contentKey: 'HELP.CONTENT_APPEARANCE',
    icon: 'palette',
    subtitleKey: 'HELP.SECTION_APPEARANCE_SUB',
    titleKey: 'HELP.SECTION_APPEARANCE',
  },
  {
    id: 'alerts-logout',
    contentKey: 'HELP.CONTENT_ALERTS_LOGOUT',
    icon: 'notifications',
    subtitleKey: 'HELP.SECTION_ALERTS_LOGOUT_SUB',
    titleKey: 'HELP.SECTION_ALERTS_LOGOUT',
  },
  {
    id: 'quiet',
    contentKey: 'HELP.CONTENT_QUIET',
    icon: 'notifications_paused',
    subtitleKey: 'HELP.SECTION_QUIET_SUB',
    titleKey: 'HELP.SECTION_QUIET',
  },
  {
    id: 'discord-telegram',
    contentKey: 'HELP.CONTENT_APPS',
    icon: 'forum',
    subtitleKey: 'HELP.SECTION_APPS_SUB',
    titleKey: 'HELP.SECTION_APPS',
  },
  {
    id: 'linked-accounts',
    contentKey: 'HELP.CONTENT_LINKED',
    icon: 'link',
    subtitleKey: 'HELP.SECTION_LINKED_SUB',
    titleKey: 'HELP.SECTION_LINKED',
  },
  {
    id: 'webhooks',
    contentKey: 'HELP.CONTENT_WEBHOOKS',
    icon: 'webhook',
    subtitleKey: 'HELP.SECTION_WEBHOOKS_SUB',
    titleKey: 'HELP.SECTION_WEBHOOKS',
  },
  {
    id: 'faq',
    contentKey: 'HELP.CONTENT_FAQ',
    icon: 'help_outline',
    subtitleKey: 'HELP.SECTION_FAQ_SUB',
    titleKey: 'HELP.SECTION_FAQ',
  },
  {
    id: 'admin',
    adminOnly: true,
    contentKey: 'HELP.CONTENT_ADMIN',
    icon: 'admin_panel_settings',
    subtitleKey: 'HELP.SECTION_ADMIN_SUB',
    titleKey: 'HELP.SECTION_ADMIN',
  },
];

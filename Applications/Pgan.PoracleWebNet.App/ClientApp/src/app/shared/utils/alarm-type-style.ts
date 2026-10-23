/** The icon and colour an alarm type wears in the sidenav. */
export interface AlarmTypeStyle {
  color: string;
  icon: string;
}

/**
 * Each alarm type's nav icon and colour, keyed by the `disable_*` setting that switches it off. The admin
 * settings page puts these on the Alarm types rows, so a switch carries the look of the page it hides.
 * They mirror the alarm items in `app.ts`; `alarm-type-style.spec.ts` fails if the two drift.
 */
export const ALARM_TYPE_STYLE: Record<string, AlarmTypeStyle> = {
  disable_raids: { color: '#f44336', icon: 'shield' },
  disable_fort_changes: { color: '#795548', icon: 'domain' },
  disable_gyms: { color: '#00bcd4', icon: 'fitness_center' },
  disable_invasions: { color: '#607d8b', icon: 'warning' },
  disable_lures: { color: '#e91e63', icon: 'place' },
  disable_maxbattles: { color: '#d500f9', icon: 'flash_on' },
  disable_mons: { color: '#4caf50', icon: 'catching_pokemon' },
  disable_nests: { color: '#8bc34a', icon: 'park' },
  disable_quests: { color: '#9c27b0', icon: 'assignment' },
  disable_showcase: { color: '#03aeb6', icon: 'celebration' },
};

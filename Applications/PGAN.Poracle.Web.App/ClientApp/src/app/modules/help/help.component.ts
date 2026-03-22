import { ChangeDetectionStrategy, Component, computed, signal, viewChildren } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatExpansionModule, MatExpansionPanel } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';

interface HelpSection {
  content: string;
  icon: string;
  iconColor: string;
  id: string;
  subtitle: string;
  title: string;
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatExpansionModule, MatIconModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  selector: 'app-help',
  styleUrl: './help.component.scss',
  templateUrl: './help.component.html',
})
export class HelpComponent {
  protected readonly searchQuery = signal('');
  protected readonly sections: HelpSection[] = [
    {
      id: 'getting-started',
      content: `<p>The DM Alerts site lets you customize exactly which Pokemon GO notifications you receive as direct messages. Instead of getting every alert, you choose what matters to you — specific Pokemon, raids, quests, and more — and only get notified about those.</p>
<div class="callout callout-info"><span class="callout-icon">&#x2139;&#xFE0F;</span><div><strong>Before you can use the site</strong>, you need to register with the Poracle bot on Discord or Telegram first. Once registered, come back here and sign in.</div></div>
<h4>Signing In</h4>
<ul>
<li><strong>Discord</strong> — Click "Sign in with Discord" on the login page. You'll be taken to Discord to authorize the app, then redirected back automatically.</li>
<li><strong>Telegram</strong> — If enabled, use the Telegram login widget on the login page. Confirm the login in your Telegram app.</li>
</ul>
<img class="help-screenshot" src="assets/help/login.png" alt="Login page with Sign in with Discord button" />
<h4>First-Time Setup</h4>
<p>When you first sign in, a welcome wizard walks you through three steps:</p>
<ol>
<li><strong>Set your location</strong> — Used to calculate distances for nearby notifications.</li>
<li><strong>Choose your areas</strong> — Select the geographic zones you want alerts from.</li>
<li><strong>Add your first alarm</strong> — Create a Pokemon, Raid, or Quest alarm to start getting notified.</li>
</ol>
<img class="help-screenshot" src="assets/help/onboarding.png" alt="Onboarding wizard showing the three setup steps" />
<p>You can skip any step and come back later. The wizard won't appear again once you dismiss it or complete all steps.</p>`,
      icon: 'rocket_launch',
      iconColor: '#1976d2',
      subtitle: 'Login, onboarding wizard, and initial setup',
      title: 'Getting Started',
    },
    {
      id: 'dashboard',
      content: `<img class="help-screenshot" src="assets/help/dashboard-overview.png" alt="Dashboard showing location, areas, profile cards and alarm counts" />
<p>The Dashboard is your home base. It shows an overview of your current setup at a glance.</p>
<h4>Status Cards</h4>
<ul>
<li><strong>Location</strong> — Shows your saved coordinates or address. Click to set or update your location.</li>
<li><strong>Active Areas</strong> — Shows how many areas you're tracking. Click to manage your areas.</li>
<li><strong>Profile</strong> — Shows your active profile. If you have multiple profiles, click to switch between them.</li>
</ul>
<h4>Active Filters</h4>
<p>A grid of cards shows how many alarms you have for each type (Pokemon, Raids, Quests, etc.). Click any card to jump to that alarm list.</p>
<h4>Quick Actions</h4>
<p>Shortcut buttons to add Pokemon, Raid, or Quest alarms, manage areas, or configure cleaning — all without navigating through the sidebar.</p>
<h4>Tips</h4>
<p>Helpful reminders appear when your setup is incomplete — like missing location, no areas selected, or no alarms configured. Each tip has an action button to fix it. You can dismiss tips you don't need.</p>
<h4>Navigation</h4>
<p>Use the sidebar to navigate between sections. Alarm types are listed at the top, followed by settings like Areas, Geofences, Profiles, and Cleaning. Help is always at the bottom.</p>
<img class="help-screenshot help-screenshot-sm" src="assets/help/sidenav.png" alt="Sidebar navigation showing Alarms and Settings sections" />`,
      icon: 'dashboard',
      iconColor: '#1976d2',
      subtitle: 'Your overview of alarms, areas, and status',
      title: 'Dashboard',
    },
    {
      id: 'location',
      content: `<img class="help-screenshot" src="assets/help/dashboard-overview.png" alt="Dashboard showing the location card with map thumbnail and address" />
<p>Your location is used for distance-based notifications. When an alarm uses "Set Distance" mode, you'll get notified about events within a radius of this location.</p>
<h4>Setting Your Location</h4>
<p>Open the location dialog from the Dashboard or Areas page. You have four ways to set it:</p>
<ul>
<li><strong>Search by address</strong> — Type an address, city, or landmark name. Select from the suggestions that appear.</li>
<li><strong>Enter coordinates</strong> — Type latitude and longitude directly if you know them.</li>
<li><strong>Use your GPS</strong> — Click "Use My Location" to use your device's current location. Your browser will ask for permission.</li>
<li><strong>Click the map</strong> — Click anywhere on the mini-map to set that point as your location.</li>
</ul>
<p>After selecting a location, the address is shown automatically. Click <strong>Save</strong> to confirm.</p>
<div class="callout callout-tip"><span class="callout-icon">&#x1F4A1;</span><div>You can clear your location from the Areas page if you only want area-based alerts.</div></div>`,
      icon: 'my_location',
      iconColor: '#2196f3',
      subtitle: 'GPS, address search, and coordinates',
      title: 'Setting Your Location',
    },
    {
      id: 'areas',
      content: `<img class="help-screenshot" src="assets/help/areas.png" alt="Areas and Location page showing area selection and location card" />
<p>Areas are predefined geographic zones set up by your community. When an alarm uses "Use Areas" mode, you get notified about events that happen inside your selected areas.</p>
<h4>Selecting Areas</h4>
<p>Go to <strong>Areas & Location</strong> from the sidebar. You can select areas two ways:</p>
<ul>
<li><strong>Map view</strong> — Click colored polygons on the map to select or deselect areas. Selected areas turn green. Hover over any area to see its name.</li>
<li><strong>List view</strong> — Use checkboxes to pick areas from a searchable list.</li>
</ul>
<h4>Region Filtering</h4>
<p>If your community has many areas across different regions, use the region dropdown to zoom in on a specific region. This makes it easier to find areas near you.</p>
<h4>Nested Areas</h4>
<p>Some areas overlap — a smaller zone inside a larger one. Both are clickable. Zoom in to make it easier to click the smaller area.</p>
<h4>Saving</h4>
<p>A save bar appears at the bottom when you've made changes. Click <strong>Save</strong> to confirm your selections, or <strong>Cancel</strong> to revert.</p>`,
      icon: 'map',
      iconColor: '#ff9800',
      subtitle: 'Map view, list view, and region filtering',
      title: 'Choosing Your Areas',
    },
    {
      id: 'geofences',
      content: `<img class="help-screenshot" src="assets/help/geofences.png" alt="My Geofences page with map and Draw Geofence button" />
<p>If the predefined areas don't cover where you want alerts, you can draw your own custom geofence boundaries on the map.</p>
<h4>Drawing a Geofence</h4>
<ol>
<li>Go to <strong>My Geofences</strong> from the sidebar.</li>
<li>Click <strong>Draw Geofence</strong>.</li>
<li>Click on the map to place points of your polygon boundary. Click the first point again to close the shape (minimum 3 points).</li>
<li>Give your geofence a name and select which region it belongs to. The region is usually auto-detected for you.</li>
<li>Click <strong>Save</strong>.</li>
</ol>
<h4>Managing Geofences</h4>
<ul>
<li><strong>Edit</strong> — Rename your geofence or change its region.</li>
<li><strong>Delete</strong> — Remove a geofence you no longer need.</li>
</ul>
<h4>Submitting for Public Approval</h4>
<p>If you think your geofence would be useful for the whole community, you can submit it for admin review. If approved, it becomes a public area everyone can select. Your private geofence continues working while the review is pending.</p>
<h4>Status Badges</h4>
<ul>
<li><strong>Active</strong> — Your private geofence, working for you only.</li>
<li><strong>Pending Review</strong> — Submitted and waiting for admin review.</li>
<li><strong>Approved</strong> — Promoted to a public area.</li>
<li><strong>Rejected</strong> — Not approved. You can see the admin's feedback and the geofence remains active as a private zone.</li>
</ul>
<div class="callout callout-info"><span class="callout-icon">&#x2139;&#xFE0F;</span><div>You can have up to <strong>10 custom geofences</strong>, each with up to 500 boundary points.</div></div>`,
      icon: 'draw',
      iconColor: '#2196f3',
      subtitle: 'Draw boundaries, submit for public approval',
      title: 'Custom Geofences',
    },
    {
      id: 'pokemon',
      content: `<img class="help-screenshot" src="assets/help/pokemon-list.png" alt="Pokemon alarms page with filter options and Add Pokemon button" />
<p>Pokemon alarms notify you when a wild Pokemon spawns that matches your filters.</p>
<h4>Adding a Pokemon Alarm</h4>
<img class="help-screenshot" src="assets/help/pokemon-add-dialog.png" alt="Add Pokemon Alarm dialog showing Pokemon selector with type filters" />
<ol>
<li>Go to <strong>Pokemon</strong> from the sidebar and click the <strong>+</strong> button.</li>
<li><strong>Select Pokemon</strong> — Search by name or Pokedex number, or use the generation and type filter buttons to browse. You can select multiple Pokemon at once.</li>
<li><strong>Set Filters</strong> — Choose what makes a spawn worth notifying about:</li>
</ol>
<ul>
<li><strong>IV range</strong> — Minimum and maximum IV percentage (0-100%)</li>
<li><strong>CP range</strong> — Filter by combat power</li>
<li><strong>Level range</strong> — Filter by Pokemon level (0-50)</li>
<li><strong>Individual stats</strong> — Filter by ATK, DEF, and STA values (0-15 each)</li>
<li><strong>Form</strong> — Track specific forms (e.g. Alolan, Galarian) or all forms</li>
<li><strong>Gender</strong> — Male, female, genderless, or all</li>
<li><strong>Weight</strong> — Filter by weight range</li>
</ul>
<h4>PVP Filters</h4>
<p>Get notified when a Pokemon has great PVP IVs. Select a league (Great, Ultra, or Little Cup) and set the rank range you care about (e.g. rank 1-50).</p>
<h4>"All Pokemon" Alarm</h4>
<div class="callout callout-tip"><span class="callout-icon">&#x1F4A1;</span><div>Select "All Pokemon" (ID 0) to create one alarm that covers every species. Useful with a high IV filter like 96-100% to catch any valuable spawn.</div></div>
<h4>Reading Alarm Cards</h4>
<p>Each alarm card shows colored pills summarizing your filters at a glance:</p>
<div class="pill-legend">
<span class="pill-sample" style="background:rgba(76,175,80,0.15);color:#2e7d32">IV 90-100%</span>
<span class="pill-sample" style="background:rgba(255,152,0,0.15);color:#e65100">CP 2000+</span>
<span class="pill-sample" style="background:rgba(33,150,243,0.15);color:#0d47a1">L30-35</span>
<span class="pill-sample" style="background:rgba(156,39,176,0.15);color:#6a1b9a">PVP GL</span>
<span class="pill-sample" style="background:rgba(233,30,99,0.15);color:#c2185b">&#9794;</span>
</div>`,
      icon: 'catching_pokemon',
      iconColor: '#4caf50',
      subtitle: 'IV, CP, level, PVP, gender, and form filters',
      title: 'Pokemon Alarms',
    },
    {
      id: 'other-alarms',
      content: `<img class="help-screenshot" src="assets/help/raids.png" alt="Raids page showing Raids and Eggs tabs with Add Raid button" />
<h4>Raid & Egg Alarms</h4>
<p>Get notified when a raid boss or egg appears that you're interested in.</p>
<ul>
<li><strong>By Level</strong> — Select raid levels (1-6) or egg levels to track all raids of that tier.</li>
<li><strong>By Boss</strong> — Select specific Pokemon raid bosses you want to hunt.</li>
<li><strong>Team filter</strong> — Only notify for raids at gyms controlled by a specific team (Mystic, Valor, Instinct).</li>
</ul>
<p>Raid and Egg alarms are managed on separate tabs within the Raids page.</p>

<h4>Quest Alarms</h4>
<p>Get notified about field research tasks with specific rewards.</p>
<ul>
<li><strong>Pokemon encounters</strong> — Select Pokemon you want as quest rewards.</li>
<li><strong>Items</strong> — Track quests that reward specific items.</li>
<li><strong>Mega Energy</strong> — Track quests that give mega energy for specific Pokemon.</li>
<li><strong>Candy</strong> — Track quests that reward candy for specific Pokemon.</li>
</ul>

<h4>Invasion Alarms</h4>
<p>Get notified about Team Rocket invasions.</p>
<ul>
<li><strong>Track All</strong> — One alarm for every grunt type and leader.</li>
<li><strong>By Type</strong> — Select specific grunt types (Bug, Dragon, Fire, etc.), Rocket Leaders, or Giovanni.</li>
<li><strong>Gender</strong> — Filter by grunt gender.</li>
</ul>

<h4>Lure Alarms</h4>
<p>Get notified when a specific lure type is placed. Choose from Normal, Glacial, Mossy, Magnetic, Rainy, and Golden lures.</p>

<h4>Nest Alarms</h4>
<p>Track nesting Pokemon species. Set a <strong>minimum spawns per hour</strong> threshold so you only get notified about nests with enough activity.</p>

<h4>Gym Alarms</h4>
<p>Track gym team changes. Select which teams (Neutral, Mystic, Valor, Instinct) to monitor. Enable <strong>Slot Changes</strong> tracking to get notified when gym slots open up.</p>`,
      icon: 'shield',
      iconColor: '#f44336',
      subtitle: 'Raids, eggs, quests, rockets, lures, nests, gyms',
      title: 'Other Alarm Types',
    },
    {
      id: 'delivery',
      content: `<img class="help-screenshot" src="assets/help/pokemon-list.png" alt="Pokemon alarm cards showing delivery mode indicators — Using Areas or distance in km" />
<p>Every alarm has delivery settings that control <em>where</em> you get notified.</p>
<h4>Areas vs Distance</h4>
<p>Each alarm uses one of two delivery modes:</p>
<div class="mode-compare">
<div class="mode-card">
<div class="mode-emoji">&#x1F5FA;</div>
<strong>Use Areas</strong>
<span>Notified when events happen inside your selected areas. Good for tracking specific neighborhoods.</span>
</div>
<div class="mode-card">
<div class="mode-emoji">&#x1F4CF;</div>
<strong>Set Distance</strong>
<span>Notified within a radius (km) of your saved location. Good for tracking everything near you.</span>
</div>
</div>
<p>You can use different modes for different alarms — for example, use areas for Pokemon and distance for raids.</p>
<h4>Notification Templates</h4>
<p>If templates are enabled, you can choose how your notification messages look. The template selector shows a live preview of what your Discord DM will look like, including the embed format, fields, and images.</p>
<h4>Clean Mode</h4>
<p>When enabled, the bot automatically deletes the notification from Discord after the event expires (e.g. a Pokemon despawns or a raid ends). This keeps your DMs tidy. You can enable clean mode per-alarm or in bulk from the <strong>Cleaning</strong> page.</p>
<h4>Ping / Role Mentions</h4>
<p>If you use webhooks, you can set a Discord role to mention in the notification (e.g. @Pokemon). This is only relevant for webhook setups.</p>`,
      icon: 'tune',
      iconColor: '#607d8b',
      subtitle: 'Areas vs distance, templates, and clean mode',
      title: 'Delivery Settings',
    },
    {
      id: 'bulk',
      content: `<img class="help-screenshot" src="assets/help/pokemon-list.png" alt="Pokemon alarm list showing the toolbar with search, filters, and select mode toggle" />
<p>All alarm pages support bulk operations so you can manage many alarms at once.</p>
<h4>Select Mode</h4>
<p>Click the <strong>checklist icon</strong> in the toolbar to enter select mode. Then click individual alarm cards to select them, or use <strong>Select All</strong> to grab everything visible.</p>
<h4>Bulk Actions</h4>
<ul>
<li><strong>Update Distance</strong> — Change the delivery mode (areas or distance) for all selected alarms at once.</li>
<li><strong>Delete</strong> — Remove all selected alarms with one confirmation.</li>
</ul>
<div class="callout callout-tip"><span class="callout-icon">&#x1F4A1;</span><div>At the bottom of each alarm list, you'll also find <strong>Update All Distance</strong> and <strong>Delete All</strong> buttons that apply to every alarm of that type.</div></div>`,
      icon: 'checklist',
      iconColor: '#795548',
      subtitle: 'Select, bulk distance update, and bulk delete',
      title: 'Bulk Operations',
    },
    {
      id: 'quick-picks',
      content: `<img class="help-screenshot" src="assets/help/quick-picks.png" alt="Quick Picks page for applying pre-built alarm templates" />
<p>Quick Picks are pre-built alarm templates created by your community's admins. They let you set up common alarm configurations with one click instead of creating each alarm individually.</p>
<h4>Applying a Quick Pick</h4>
<ol>
<li>Go to <strong>Quick Picks</strong> from the sidebar.</li>
<li>Browse the available picks, optionally filtering by category.</li>
<li>Click <strong>Apply</strong> on a Quick Pick you want.</li>
<li>Customize before applying: choose your delivery mode (areas or distance), enable clean mode, and optionally exclude specific Pokemon.</li>
<li>Confirm to create all the alarms at once.</li>
</ol>
<h4>Removing Quick Pick Alarms</h4>
<p>If you no longer want alarms from a Quick Pick, click <strong>Remove</strong> to delete all alarms it created.</p>`,
      icon: 'bolt',
      iconColor: '#ff6f00',
      subtitle: 'Pre-built alarm templates for quick setup',
      title: 'Quick Picks',
    },
    {
      id: 'profiles',
      content: `<img class="help-screenshot" src="assets/help/profiles.png" alt="Profiles page showing Default Profile (active) and Work Profile cards" />
<p>Profiles let you maintain completely separate alarm configurations. Each profile has its own set of alarms, selected areas, and location.</p>
<h4>Why Use Profiles?</h4>
<p>Useful if you want different setups for different situations — for example, a "Home" profile for your neighborhood and a "Work" profile for around your office.</p>
<h4>Managing Profiles</h4>
<ul>
<li><strong>Create</strong> — Click the + button on the Profiles page. Give your profile a name (up to 32 characters).</li>
<li><strong>Switch</strong> — Click "Switch" on a profile card, or use the quick-switch dropdown on the Dashboard. Switching updates all your alarm lists to show that profile's alarms.</li>
<li><strong>Edit</strong> — Rename a profile at any time.</li>
<li><strong>Delete</strong> — Remove a profile you no longer need.</li>
</ul>
<div class="callout callout-warn"><span class="callout-icon">&#x26A0;&#xFE0F;</span><div><strong>Warning:</strong> Deleting a profile permanently removes all alarms in that profile. You can't delete your currently active profile.</div></div>`,
      icon: 'person',
      iconColor: '#7b1fa2',
      subtitle: 'Separate alarm sets for different situations',
      title: 'Profiles',
    },
    {
      id: 'cleaning',
      content: `<img class="help-screenshot" src="assets/help/cleaning.png" alt="Cleaning page with toggle switches for each alarm type" />
<p>The Cleaning page lets you control clean mode across all your alarm types at once.</p>
<p>When clean mode is on for an alarm type, the bot automatically deletes notifications from Discord after the event expires:</p>
<ul>
<li><strong>Pokemon</strong> — Deleted when the spawn despawns</li>
<li><strong>Raids</strong> — Deleted when the raid ends</li>
<li><strong>Eggs</strong> — Deleted when the egg hatches</li>
<li><strong>Quests</strong> — Deleted when quests reset at midnight</li>
<li><strong>Invasions</strong> — Deleted when the grunt leaves</li>
<li><strong>Lures</strong> — Deleted when the lure expires</li>
<li><strong>Nests</strong> — Deleted when nests migrate</li>
<li><strong>Gyms</strong> — Deleted after gym changes</li>
</ul>
<p>Use <strong>Enable All</strong> or <strong>Disable All</strong> to toggle everything at once.</p>
<div class="callout callout-tip"><span class="callout-icon">&#x1F4A1;</span><div><strong>Recommended:</strong> Keep clean mode enabled to prevent outdated alerts from piling up in your DMs.</div></div>`,
      icon: 'cleaning_services',
      iconColor: '#795548',
      subtitle: 'Auto-delete expired notifications per alarm type',
      title: 'Cleaning (Auto-Delete)',
    },
    {
      id: 'appearance',
      content: `<h4>Dark / Light Mode</h4>
<p>Click the sun/moon icon in the top toolbar to switch between dark and light themes. Your choice is saved automatically.</p>
<img class="help-screenshot help-screenshot-sm" src="assets/help/toolbar-theme.png" alt="Toolbar showing the dark/light mode toggle button" />
<h4>Accent Colors</h4>
<p>Open the user menu (your avatar in the top-right) and select <strong>Accent Theme</strong>. Choose from:</p>
<ul>
<li><strong>Default</strong> — Blue</li>
<li><strong>Pokemon</strong> — Green</li>
<li><strong>Raids</strong> — Red</li>
<li><strong>Mystic</strong> — Blue</li>
<li><strong>Valor</strong> — Red</li>
<li><strong>Instinct</strong> — Yellow</li>
</ul>
<p>The accent color changes the toolbar gradient, active navigation highlight, and other UI accents throughout the site.</p>
<img class="help-screenshot" src="assets/help/dark-mode.png" alt="Dashboard in dark mode showing the dark theme applied across the entire interface" />
<h4>Language</h4>
<p>If available, use the language selector in the toolbar to switch the interface language. 18 languages are supported.</p>
<h4>Keyboard Shortcuts</h4>
<table>
<tr><td><kbd>?</kbd></td><td>Show keyboard shortcuts</td></tr>
<tr><td><kbd>Esc</kbd></td><td>Close menus or dialogs</td></tr>
<tr><td><kbd>[</kbd></td><td>Collapse sidebar</td></tr>
<tr><td><kbd>]</kbd></td><td>Expand sidebar</td></tr>
</table>`,
      icon: 'palette',
      iconColor: '#673ab7',
      subtitle: 'Themes, accent colors, language, shortcuts',
      title: 'Appearance & Preferences',
    },
    {
      id: 'alerts-logout',
      content: `<img class="help-screenshot help-screenshot-sm" src="assets/help/user-menu.png" alt="User menu showing Pause Alerts, Switch Profile, Areas, Cleaning, Accent Theme, and Logout options" />
<h4>Pausing Alerts</h4>
<p>Open the user menu (your avatar) and click <strong>Pause Alerts</strong>. A red banner will appear at the top of the site confirming your alerts are paused. You won't receive any notifications while paused.</p>
<p>To resume, click <strong>Resume Alerts</strong> from the user menu or the banner.</p>
<h4>Logging Out</h4>
<p>Open the user menu and click <strong>Logout</strong>. You'll be returned to the login page.</p>`,
      icon: 'notifications',
      iconColor: '#ff5722',
      subtitle: 'Pause notifications and sign out',
      title: 'Pausing Alerts & Logging Out',
    },
    {
      id: 'faq',
      content: `<h4>"I can't log in"</h4>
<p>You must register with the Poracle bot on Discord or Telegram <strong>before</strong> you can sign in to this site. If you see "Your account is not registered," contact your community admin for registration instructions.</p>

<h4>"I'm not getting notifications"</h4>
<p>Check these common causes:</p>
<ol>
<li><strong>Alerts paused</strong> — Look for a red banner at the top of the site. Resume alerts from the user menu.</li>
<li><strong>No location set</strong> — If your alarms use distance mode, you need a saved location.</li>
<li><strong>No areas selected</strong> — If your alarms use areas mode, make sure you've selected areas on the Areas page.</li>
<li><strong>Wrong profile</strong> — You might have alarms on a different profile. Check which profile is active on the Dashboard.</li>
<li><strong>Filters too strict</strong> — Try relaxing your IV, CP, or level filters to see if notifications start coming through.</li>
</ol>

<h4>"My alarms disappeared"</h4>
<p>Alarms are profile-specific. If you switched profiles, your alarms from the other profile are still there — just switch back from the Dashboard or Profiles page.</p>

<h4>"I can't click a small area on the map"</h4>
<p>When areas overlap, zoom in to make the smaller area easier to click. Smaller areas are always on top of larger ones.</p>

<h4>"What does Clean mode do?"</h4>
<p>Clean mode tells the bot to automatically delete a notification from Discord after the event expires (e.g. a Pokemon despawns). Without it, old alerts stay in your DMs forever. Enable it on the Cleaning page or per-alarm in the Delivery tab.</p>

<h4>"What's the difference between Areas and Distance?"</h4>
<p>Each alarm uses one delivery mode. <strong>Areas</strong> notifies you about events inside specific geographic zones. <strong>Distance</strong> notifies you about events within a radius of your saved location. You can mix both across different alarms.</p>`,
      icon: 'help_outline',
      iconColor: '#ff9800',
      subtitle: 'Common issues and how to fix them',
      title: 'Frequently Asked Questions',
    },
  ];

  protected readonly filteredSections = computed(() => {
    const query = this.searchQuery().toLowerCase().trim();
    if (!query) return this.sections;
    return this.sections.filter(
      s => s.title.toLowerCase().includes(query) || s.subtitle.toLowerCase().includes(query) || s.content.toLowerCase().includes(query),
    );
  });

  protected readonly panels = viewChildren(MatExpansionPanel);

  protected scrollToSection(sectionId: string): void {
    const el = document.getElementById('section-' + sectionId);
    if (el) {
      el.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
    // expand the panel
    const idx = this.filteredSections().findIndex(s => s.id === sectionId);
    const panels = this.panels();
    if (idx >= 0 && panels[idx]) {
      panels[idx].open();
    }
  }
}

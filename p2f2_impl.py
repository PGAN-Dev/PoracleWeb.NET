import sys
root=sys.argv[1]
def sub(p,a,b):
    s=open(p,encoding='utf-8',newline='').read()
    assert s.count(a)==1,(p,a)
    open(p,'w',encoding='utf-8',newline='').write(s.replace(a,b))
p=root+'/core/services/template.service.ts'
sub(p,"""  getTemplatesForType(alarmType: string): Observable<(string | number)[]> {
    return this.loadTemplates().pipe(
      map(data => {
        const userType = this.auth.user()?.type || 'discord:user';
        const platform = userType.startsWith('telegram') ? 'telegram' : 'discord';""","""  /**
   * The template ids for an alarm type on one platform. `side` is the app the alarm is delivered to; left out,
   * it is the signed-in person's own platform (the only side an unlinked person has).
   */
  getTemplatesForType(alarmType: string, side?: LinkSide | null): Observable<(string | number)[]> {
    return this.loadTemplates().pipe(
      map(data => {
        const userType = this.auth.user()?.type || 'discord:user';
        const platform = side ?? (userType.startsWith('telegram') ? 'telegram' : 'discord');""")
sub(p,"import { AuthService }","import { LinkSide } from '../models';\nimport { AuthService }")
p=root+'/shared/components/template-selector/template-selector.component.ts'
sub(p,"import { Component, EventEmitter, Input, OnInit, Output, inject, signal, computed, effect } from","import { Component, EventEmitter, Input, OnInit, Output, inject, input, signal, computed, effect, untracked } from")
sub(p,"import { AuthService }","import { LinkSide } from '../../../core/models';\nimport { AuthService }")
sub(p,"  platform = computed(() => (this.auth.user()?.type?.startsWith('telegram') ? 'telegram' : 'discord'));",
"""  /**
   * The app the alarm is delivered to (the dialog's "Deliver to" value). Null or unset means the signed-in
   * person's own platform, which is every unlinked person and every dialog that has no such field.
   */
  readonly side = input<LinkSide | null>(null);
  platform = computed(() => this.side() ?? (this.auth.user()?.type?.startsWith('telegram') ? 'telegram' : 'discord'));""")
sub(p,"""    this.templateService.getTemplatesForType(this.alarmType).subscribe(t => {
      // If no templates exist for this alarm type, provide a sensible default.
      // PoracleNG uses template "1" as the default when none is configured.
      const templates = t.length > 0 ? t : [1];
      this.templates.set(templates);
      if (!this.value && templates.length > 0) {
        this.value = templates[0].toString();
        this.valueChange.emit(this.value);
      }
    });

    this.loadDts();
  }
""","""    this.started = true;
    this.loadDts();
  }
""")
sub(p,"  constructor() {\n","""  constructor() {
    // List the templates of the side the alarm goes to, and list them again when that side changes.
    effect(() => {
      const platform = this.platform();
      if (!this.started) return;
      untracked(() => this.loadTemplates(platform));
    });

""")
sub(p,"  private http = inject(HttpClient);","  private http = inject(HttpClient);\n  private listedFor: null | string = null;\n  private started = false;")
sub(p,"  private loadDts(): void {","""  private loadTemplates(platform: LinkSide): void {
    const changedSide = this.listedFor !== null && this.listedFor !== platform;
    this.listedFor = platform;
    this.templateService.getTemplatesForType(this.alarmType, platform).subscribe(t => {
      if (this.listedFor !== platform) return;
      // If no templates exist for this alarm type, provide a sensible default.
      // PoracleNG uses template "1" as the default when none is configured.
      const templates = t.length > 0 ? t : [1];
      this.templates.set(templates);
      // An empty value takes the first template. So does one the newly chosen side does not have, but only
      // after the person changed side: an alarm opened for edit keeps whatever template it was saved with.
      const missing = changedSide && !templates.some(x => x.toString() === this.value);
      if ((!this.value || missing) && templates.length > 0) {
        this.value = templates[0].toString();
        this.valueChange.emit(this.value);
      }
    });
  }

  private loadDts(): void {""")

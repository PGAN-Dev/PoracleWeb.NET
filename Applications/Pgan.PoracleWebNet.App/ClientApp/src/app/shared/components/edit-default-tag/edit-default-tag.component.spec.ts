import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { EditDefaultTagComponent } from './edit-default-tag.component';
import { PoracleConfigService } from '../../../core/services/poracle-config.service';

describe('EditDefaultTagComponent', () => {
  let fixture: ComponentFixture<EditDefaultTagComponent>;
  let serverConfig: ReturnType<typeof signal<{ defaultEdit?: string[] }>>;
  let load: jest.Mock;

  beforeEach(() => {
    serverConfig = signal<{ defaultEdit?: string[] }>({ defaultEdit: ['pokemon', 'lure'] });
    load = jest.fn(() => of(serverConfig()));
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService(), { provide: PoracleConfigService, useValue: { load, serverConfig } }],
      imports: [EditDefaultTagComponent],
    });
    fixture = TestBed.createComponent(EditDefaultTagComponent);
  });

  const tag = () => fixture.nativeElement.querySelector('.rule-tag') as HTMLElement | null;
  const hint = () => fixture.nativeElement.querySelector('.clean-hint') as HTMLElement | null;

  it('shows the server-default tag for a listed type whose rule has no edit bit', () => {
    fixture.componentRef.setInput('type', 'pokemon');
    fixture.componentRef.setInput('clean', 1);
    fixture.detectChanges();
    expect(tag()?.textContent).toContain('ALARM.STATUS_EDIT_DEFAULT');
  });

  it('stays hidden when the rule carries its own edit bit', () => {
    fixture.componentRef.setInput('type', 'pokemon');
    fixture.componentRef.setInput('clean', 3);
    fixture.detectChanges();
    expect(tag()).toBeNull();
  });

  it('stays hidden for a type the server does not list', () => {
    fixture.componentRef.setInput('type', 'raid');
    fixture.componentRef.setInput('clean', 0);
    fixture.detectChanges();
    expect(tag()).toBeNull();
  });

  it('stays hidden while the type cannot edit at all, however the list reads', () => {
    fixture.componentRef.setInput('type', 'pokemon');
    fixture.componentRef.setInput('enabled', false);
    fixture.detectChanges();
    expect(tag()).toBeNull();
  });

  it('stays hidden on a server that publishes no list at all', () => {
    serverConfig.set({});
    fixture.componentRef.setInput('type', 'pokemon');
    fixture.detectChanges();
    expect(tag()).toBeNull();
  });

  it('asks for the server config so a list page that never did still gets the answer', () => {
    fixture.componentRef.setInput('type', 'pokemon');
    fixture.detectChanges();
    expect(load).toHaveBeenCalledTimes(1);
  });

  it('follows the config once it arrives', () => {
    serverConfig.set({ defaultEdit: [] });
    fixture.componentRef.setInput('type', 'lure');
    fixture.detectChanges();
    expect(tag()).toBeNull();

    serverConfig.set({ defaultEdit: ['lure'] });
    fixture.detectChanges();
    expect(tag()).not.toBeNull();
  });

  it('renders the dialog hint for a listed type whatever the bit says', () => {
    fixture.componentRef.setInput('appearance', 'hint');
    fixture.componentRef.setInput('type', 'lure');
    fixture.componentRef.setInput('clean', 2);
    fixture.detectChanges();
    expect(hint()?.textContent).toContain('ALARM.EDIT_DEFAULT_DIALOG_HINT');
    expect(tag()).toBeNull();
  });

  it('renders no dialog hint for an unlisted type', () => {
    fixture.componentRef.setInput('appearance', 'hint');
    fixture.componentRef.setInput('type', 'egg');
    fixture.detectChanges();
    expect(hint()).toBeNull();
  });
});

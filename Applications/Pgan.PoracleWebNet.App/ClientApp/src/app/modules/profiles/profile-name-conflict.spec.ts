import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { ProfileAddDialogComponent } from './profile-add-dialog.component';
import { ProfileDuplicateDialogComponent } from './profile-duplicate-dialog.component';
import { ProfileEditDialogComponent } from './profile-edit-dialog.component';
import { ProfileService } from '../../core/services/profile.service';

/**
 * A bare ngModel never puts a form field in error state, so a mat-error under it never renders. The add dialog
 * learned that in #427; the edit dialog kept the mat-error, and renaming a profile to a taken name did nothing
 * and said nothing. Every name dialog shows the clash in the hint slot and turns its button off.
 */
describe('profile name dialogs show a name clash', () => {
  const profiles = [
    { name: 'Default', profileNo: 1 },
    { name: 'Work', profileNo: 2 },
  ];

  const setup = <T>(component: Type<T>, service: Record<string, jest.Mock> = {}) => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideNoopAnimations(),
        provideTranslateService(),
        { provide: ProfileService, useValue: { getAll: jest.fn(() => of(profiles)), ...service } },
        { provide: MatDialogRef, useValue: { close: jest.fn() } },
        { provide: MAT_DIALOG_DATA, useValue: { name: 'Work', profileNo: 2 } },
      ],
      imports: [component],
    });
    const fixture = TestBed.createComponent(component);
    fixture.detectChanges();
    return fixture;
  };

  const type = async (fixture: ReturnType<typeof setup>, value: string) => {
    const input: HTMLInputElement = fixture.nativeElement.querySelector('input[maxlength]');
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  const saveButton = (fixture: ReturnType<typeof setup>): HTMLButtonElement =>
    fixture.nativeElement.querySelector('button[color="primary"]');

  const cases: [string, Type<unknown>, Record<string, jest.Mock>][] = [
    ['edit', ProfileEditDialogComponent, { update: jest.fn() }],
    ['add', ProfileAddDialogComponent, { create: jest.fn() }],
    ['duplicate', ProfileDuplicateDialogComponent, { duplicate: jest.fn() }],
  ];

  describe.each(cases)('%s dialog', (_label, component, service) => {
    it('renders the clash message and keeps its button off for a taken name, in any case', async () => {
      const fixture = setup(component, service);
      await type(fixture, 'default');

      const hint = fixture.nativeElement.querySelector('.name-conflict');
      expect(hint?.textContent).toContain('DIALOG.PROMPT_CONFLICT');
      expect(saveButton(fixture).disabled).toBe(true);
      expect(fixture.nativeElement.textContent).not.toContain('/32');
    });

    it('shows the counter and allows saving for a free name', async () => {
      const fixture = setup(component, service);
      await type(fixture, 'Weekend');

      expect(fixture.nativeElement.querySelector('.name-conflict')).toBeNull();
      expect(fixture.nativeElement.textContent).toContain('7/32');
      expect(saveButton(fixture).disabled).toBe(false);
    });
  });

  it("edit: keeping the profile's own name is not a clash", async () => {
    const fixture = setup(ProfileEditDialogComponent, { update: jest.fn() });
    await type(fixture, 'Work');

    expect(fixture.nativeElement.querySelector('.name-conflict')).toBeNull();
    expect(saveButton(fixture).disabled).toBe(false);
  });
});

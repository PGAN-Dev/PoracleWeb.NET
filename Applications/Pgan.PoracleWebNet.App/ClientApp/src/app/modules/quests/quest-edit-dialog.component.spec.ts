import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { TranslateModule } from '@ngx-translate/core';
import { of } from 'rxjs';

import { QuestEditDialogComponent } from './quest-edit-dialog.component';
import { Quest, QuestUpdate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { IconService } from '../../core/services/icon.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { QuestService } from '../../core/services/quest.service';

describe('QuestEditDialogComponent', () => {
  let component: QuestEditDialogComponent;
  let dialogRef: { close: jest.Mock };
  let questService: { update: jest.Mock };

  const baseQuest: Quest = {
    id: 'quest-1',
    uid: 77,
    clean: 0,
    distance: 0,
    ping: null,
    pokemonId: 25,
    profileNo: 1,
    reward: 25,
    rewardType: 7,
    shiny: 0,
    template: null,
  };

  function setup(data: Quest) {
    dialogRef = { close: jest.fn() };
    questService = { update: jest.fn().mockReturnValue(of(void 0)) };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: QuestService, useValue: questService },
        { provide: AuthService, useValue: { isImpersonating: () => false, user: () => ({ type: 'discord:user' }) } },
        {
          provide: MasterDataService,
          useValue: { getItemName: () => 'Item', getPokemonName: () => 'Pikachu', loadData: () => of(void 0) },
        },
        { provide: IconService, useValue: { getItemUrl: () => '', getPokemonUrl: () => '', getRewardUrl: () => '' } },
      ],
      imports: [QuestEditDialogComponent, TranslateModule.forRoot()],
    });

    const fixture = TestBed.createComponent(QuestEditDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function savedClean(): number {
    const update = questService.update.mock.calls[0][1] as QuestUpdate;
    return update.clean as number;
  }

  describe('form init from clean bits', () => {
    it('initializes both toggles off when clean=0', () => {
      setup({ ...baseQuest, clean: 0 });
      expect(component.form.controls.clean.value).toBe(false);
      expect(component.form.controls.summary.value).toBe(false);
    });

    it('initializes auto-delete on, summary off when clean=1', () => {
      setup({ ...baseQuest, clean: 1 });
      expect(component.form.controls.clean.value).toBe(true);
      expect(component.form.controls.summary.value).toBe(false);
    });

    it('initializes summary on from bit 4 when clean=4', () => {
      setup({ ...baseQuest, clean: 4 });
      expect(component.form.controls.clean.value).toBe(false);
      expect(component.form.controls.summary.value).toBe(true);
    });

    it('initializes both on when clean=5', () => {
      setup({ ...baseQuest, clean: 5 });
      expect(component.form.controls.clean.value).toBe(true);
      expect(component.form.controls.summary.value).toBe(true);
    });

    it('initializes summary on from bit 4 even when an edit-in-place bit is also set (clean=6)', () => {
      setup({ ...baseQuest, clean: 6 });
      expect(component.form.controls.clean.value).toBe(false);
      expect(component.form.controls.summary.value).toBe(true);
    });
  });

  describe('save composes bit 4 while preserving other bits', () => {
    it('sets bit 4 when toggled on, leaving auto-delete off (clean 0 -> 4)', () => {
      setup({ ...baseQuest, clean: 0 });
      component.form.controls.summary.setValue(true);
      component.save();
      expect(savedClean()).toBe(4);
    });

    it('combines auto-delete + summary (clean 0 -> 5)', () => {
      setup({ ...baseQuest, clean: 0 });
      component.form.controls.clean.setValue(true);
      component.form.controls.summary.setValue(true);
      component.save();
      expect(savedClean()).toBe(5);
    });

    it('clears bit 4 when toggled off (clean 5 -> 1)', () => {
      setup({ ...baseQuest, clean: 5 });
      component.form.controls.summary.setValue(false);
      component.save();
      expect(savedClean()).toBe(1);
    });

    it('preserves an unsurfaced edit-in-place bit when editing (clean 3 -> 7)', () => {
      // clean=3 => auto-delete (1) + edit-in-place (2); turning summary on must keep bit 2.
      setup({ ...baseQuest, clean: 3 });
      component.form.controls.summary.setValue(true);
      component.save();
      expect(savedClean()).toBe(7);
    });

    it('preserves the edit-in-place bit when turning auto-delete off (clean 3 -> 2)', () => {
      setup({ ...baseQuest, clean: 3 });
      component.form.controls.clean.setValue(false);
      component.save();
      expect(savedClean()).toBe(2);
    });

    it('passes the composed clean and uid to the service', () => {
      setup({ ...baseQuest, clean: 0 });
      component.form.controls.summary.setValue(true);
      component.save();
      expect(questService.update).toHaveBeenCalledWith(77, expect.objectContaining({ clean: 4 }));
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });
  });
});

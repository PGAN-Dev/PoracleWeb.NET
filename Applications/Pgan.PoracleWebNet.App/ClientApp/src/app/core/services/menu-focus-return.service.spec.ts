import { Component, inject } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatMenuModule } from '@angular/material/menu';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import { MenuFocusReturnService } from './menu-focus-return.service';

@Component({ selector: 'app-probe-dialog', template: '<p>probe</p><button type="button" id="inside">inside</button>' })
class ProbeDialogComponent {}

@Component({
  imports: [MatMenuModule, MatDialogModule],
  selector: 'app-host',
  template: `
    <button id="outside" type="button">outside</button>
    <button id="trigger" type="button" [matMenuTriggerFor]="menu">more</button>
    <mat-menu #menu="matMenu">
      <button mat-menu-item id="sync-item" type="button" (click)="open()">Sync dialog</button>
      <button mat-menu-item id="plain-item" type="button">No dialog</button>
    </mat-menu>
  `,
})
class HostComponent {
  private readonly dialog = inject(MatDialog);
  lastRef?: ReturnType<MatDialog['open']>;

  open(): void {
    this.lastRef = this.dialog.open(ProbeDialogComponent);
  }
}

describe('MenuFocusReturnService', () => {
  let fixture: ComponentFixture<HostComponent>;

  const trigger = () => document.getElementById('trigger') as HTMLButtonElement;
  const clickItem = async (id: string) => {
    trigger().click();
    fixture.detectChanges();
    await fixture.whenStable();
    (document.getElementById(id) as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();
  };
  const closeDialog = async () => {
    fixture.componentInstance.lastRef!.close();
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise(r => setTimeout(r, 0));
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ imports: [HostComponent, NoopAnimationsModule] });
    TestBed.inject(MenuFocusReturnService).init();
    fixture = TestBed.createComponent(HostComponent);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
  });

  afterEach(() => {
    TestBed.inject(MatDialog).closeAll();
    fixture.nativeElement.remove();
  });

  it('puts focus back on the menu trigger when a dialog opened from a menu item closes', async () => {
    await clickItem('sync-item');
    expect(document.querySelector('app-probe-dialog')).not.toBeNull();

    await closeDialog();

    expect(document.activeElement).toBe(trigger());
  });

  it('leaves a dialog opened from a plain button alone', async () => {
    const outside = document.getElementById('outside') as HTMLButtonElement;
    outside.focus();
    outside.click();
    fixture.componentInstance.open();
    fixture.detectChanges();
    await fixture.whenStable();

    await closeDialog();

    expect(document.activeElement).toBe(outside);
  });

  it('does not steal focus for a later, unrelated dialog after a menu item that opened none', async () => {
    const outside = document.getElementById('outside') as HTMLButtonElement;
    await clickItem('plain-item');
    await new Promise(r => setTimeout(r, 0));
    outside.focus();
    fixture.componentInstance.open();
    fixture.detectChanges();
    await fixture.whenStable();

    await closeDialog();

    expect(document.activeElement).toBe(outside);
  });

  it('does not try to focus a trigger that has left the page', async () => {
    await clickItem('sync-item');
    trigger().remove();

    await closeDialog();

    expect(document.activeElement).toBe(document.body);
  });
});

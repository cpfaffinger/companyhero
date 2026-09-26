// Austritt (Zugang 8): Bestätigung vor dem sofortigen Ende aller Sitzungen und Identitäten; die Datenfolgen regelt Datenschutz 5.2.
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatDialogActions, MatDialogClose, MatDialogContent, MatDialogTitle } from '@angular/material/dialog';
import { TextService } from '../../../../libs/theme/text.service';

@Component({
  selector: 'ch-austritt-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButton, MatDialogTitle, MatDialogContent, MatDialogActions, MatDialogClose],
  template: `
    <h2 mat-dialog-title>{{ texts.t('ich.zugang.austrittTitel') }}</h2>
    <mat-dialog-content>{{ texts.t('ich.zugang.austrittText') }}</mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton type="button" mat-dialog-close>{{ texts.t('form.abbrechen') }}</button>
      <button matButton="filled" type="button" [mat-dialog-close]="true" data-testid="austritt-bestaetigen">{{ texts.t('ich.zugang.austreten') }}</button>
    </mat-dialog-actions>
  `,
})
export class AustrittDialog {
  protected readonly texts = inject(TextService);
}

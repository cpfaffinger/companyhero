// Dialog aus Material (K02, K08): Bestätigung vor dem Löschen eines Beitrags (Challenges 3: Korrektur als Gegenbuchung,
// Abzeichen bleiben). Die Löschung selbst folgt mit dem Fachpfad; der Dialog belegt Theme, Fokus und Tastatur im Overlay (K05).
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatDialogActions, MatDialogClose, MatDialogContent, MatDialogTitle } from '@angular/material/dialog';
import { TextService } from '../../../../libs/theme/text.service';

@Component({
  selector: 'ch-beitrag-loeschen-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButton, MatDialogTitle, MatDialogContent, MatDialogActions, MatDialogClose],
  template: `
    <h2 mat-dialog-title>{{ texts.t('dialog.loeschenTitel') }}</h2>
    <mat-dialog-content>{{ texts.t('dialog.loeschenText') }}</mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton type="button" mat-dialog-close>{{ texts.t('form.abbrechen') }}</button>
      <button matButton="filled" type="button" [mat-dialog-close]="true">{{ texts.t('dialog.loeschen') }}</button>
    </mat-dialog-actions>
  `,
})
export class BeitragLoeschenDialog {
  protected readonly texts = inject(TextService);
}

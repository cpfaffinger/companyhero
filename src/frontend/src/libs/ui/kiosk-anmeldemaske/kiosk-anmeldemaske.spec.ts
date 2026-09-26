import { TestBed } from '@angular/core/testing';
import { KioskAnmeldemaske, type KioskAnmeldung } from './kiosk-anmeldemaske';
import { ThemeService, type TenantTheme } from '../../theme/theme.service';
import themeWiesner from '../../../../e2e/fixtures/api/theme-wiesner.json';

describe('KioskAnmeldemaske (Zugang 6.2, A-018)', () => {
  function render() {
    TestBed.configureTestingModule({ imports: [KioskAnmeldemaske] });
    TestBed.inject(ThemeService).tenantTheme.set(themeWiesner.body as TenantTheme);
    const fixture = TestBed.createComponent(KioskAnmeldemaske);
    fixture.detectChanges();
    return fixture;
  }

  it('nimmt sechs Ziffern Kennung, dann vier Ziffern PIN über den Ziffernblock und meldet beide', () => {
    const fixture = render();
    const root = fixture.nativeElement as HTMLElement;
    let result: KioskAnmeldung | undefined;
    fixture.componentInstance.anmelden.subscribe((a) => (result = a));
    const key = (label: string) => [...root.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.trim() === label)!;
    for (const d of ['4', '8', '1', '2', '0', '7']) {
      key(d).click();
    }
    fixture.detectChanges();
    expect(root.querySelector<HTMLInputElement>('#ch-kiosk-kennung')?.value).toBe('481207');
    expect(fixture.componentInstance.stage()).toBe('pin');
    for (const d of ['1', '2', '3']) {
      key(d).click();
    }
    fixture.detectChanges();
    expect(root.querySelectorAll('.ch-kiosk__pin-slot').length).toBe(4);
    expect(key('Weiter').disabled).toBe(true);
    key('4').click();
    fixture.detectChanges();
    expect(key('Weiter').disabled).toBe(false);
    key('Weiter').click();
    expect(result).toEqual({ kennung: '481207', pin: '1234' });
  });

  it('zeigt keine Namensliste und keine PIN im Klartext; Löschen wirkt zuerst auf die PIN', () => {
    const fixture = render();
    const root = fixture.nativeElement as HTMLElement;
    fixture.componentInstance.kennung.set('123456');
    fixture.componentInstance.pin.set('12');
    fixture.detectChanges();
    expect(root.textContent).not.toContain('12 ');
    expect(root.querySelectorAll('.ch-kiosk__pin-slot')[0].textContent?.trim()).toBe('●');
    expect(root.querySelector('select, [role="listbox"], ul')).toBeNull();
    fixture.componentInstance.erase();
    expect(fixture.componentInstance.pin()).toBe('1');
    expect(fixture.componentInstance.kennung()).toBe('123456');
  });
});

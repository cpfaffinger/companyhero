import { TestBed } from '@angular/core/testing';
import { ChallengeCard } from './challenge-card';
import { ThemeService, type TenantTheme } from '../../theme/theme.service';
import type { ChallengeCard as ChallengeCardData } from '../../data-access/challenges/challenges.api';
import challenges from '../../../../e2e/fixtures/api/challenges.json';
import themeWiesner from '../../../../e2e/fixtures/api/theme-wiesner.json';
import themeHoedl from '../../../../e2e/fixtures/api/theme-hoedl.json';

describe('ChallengeCard (Marke 2.5, Challenges 6.2, A-080)', () => {
  const card = challenges.body[0] as ChallengeCardData;
  // Vier Minuten nach dem Kollektivstand der Probe; die Probe endet neun Tage nach ihrer Aufnahme.
  const now = new Date(new Date(card.collective!.updatedAt).getTime() + 4 * 60_000);

  function render(data: ChallengeCardData, theme: TenantTheme) {
    TestBed.configureTestingModule({ imports: [ChallengeCard] });
    TestBed.inject(ThemeService).tenantTheme.set(theme);
    const fixture = TestBed.createComponent(ChallengeCard);
    fixture.componentRef.setInput('card', data);
    fixture.componentRef.setInput('now', now);
    fixture.detectChanges();
    return fixture;
  }

  it('zeigt Sammelziel, Meilensteine, Altersangabe und Prozent aus dem Backend mit Textentsprechung', () => {
    const fixture = render(card, themeWiesner.body as TenantTheme);
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('h2')?.textContent).toContain('Rad oder Fuß zur Arbeit');
    expect(root.textContent).toContain('Sammelziel · Häkchen · ganze Firma');
    expect(root.textContent).toContain('noch 9 Tage');
    expect(root.textContent).toContain('Stand vor 4 Min');
    expect(root.textContent).toContain('12 %');
    expect(root.querySelector('ch-collective-bar')?.getAttribute('aria-label')).toBe('12 Prozent des Firmenziels erreicht, nächster Meilenstein bei 25 Prozent');
    expect(root.querySelectorAll('.ch-bar__milestone').length).toBe(4);
    expect(root.textContent).toContain('Dein Beitrag heute');
    expect(root.textContent).toContain('noch offen');
    expect(root.querySelector('button')?.textContent).toContain('Heute erledigt');
  });

  it('spricht in der Anrede des Tenants und zeigt nach dem Beitrag den Erfolgsmoment statt der Aktion', () => {
    const fixture = render({ ...card, contributedToday: true, percent: 16 }, themeHoedl.body as TenantTheme);
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('Ihr Beitrag heute');
    expect(root.querySelector('button')).toBeNull();
    expect(root.querySelector('ch-progress-ring')).not.toBeNull();
  });

  it('meldet die Aktion nach außen und rechnet keine Prozente selbst', () => {
    const fixture = render(card, themeWiesner.body as TenantTheme);
    let emitted: ChallengeCardData | undefined;
    fixture.componentInstance.contribute.subscribe((c) => (emitted = c));
    (fixture.nativeElement as HTMLElement).querySelector('button')?.click();
    expect(emitted?.challengeId).toBe(card.challengeId);
    expect(fixture.componentInstance.percent()).toBe(card.percent);
  });
});

import { TestBed } from '@angular/core/testing';
import { App } from './app';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [App] }).compileComponents();
  });

  it('zeigt den Produktnamen als Überschrift', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const heading = (fixture.nativeElement as HTMLElement).querySelector('h1');
    expect(heading?.textContent).toContain('CompanyHero');
  });
});

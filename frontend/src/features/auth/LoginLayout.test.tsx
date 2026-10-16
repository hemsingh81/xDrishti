import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http } from 'msw';
import { Suspense } from 'react';
import { appliedCss } from '@/test/css';
import { buildProblem } from '@/test/fixtures';
import { renderRoutes } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { LoginPage } from './index';

const routes = [
  {
    path: '/login',
    element: (
      <Suspense>
        <LoginPage />
      </Suspense>
    ),
  },
];

const renderLogin = () => {
  server.use(http.get('/api/auth/session', () => problemResponse(buildProblem('auth.unauthenticated', 401), 401)));
  return renderRoutes(routes, { initialPath: '/login' });
};

const below860 = (element: Element) =>
  appliedCss(element).responsive.find((r) => /max-width:\s*860/.test(r.query))?.css ?? '';

describe('sign-in page layout', () => {
  it('has two panels: the hero (brand, headline, what the app does) and the form', async () => {
    renderLogin();

    const hero = await screen.findByRole('region', { name: 'About xDrishti' });
    expect(within(hero).getByText('xDrishti')).toBeInTheDocument();
    expect(within(hero).getByText('दृष्टि · vision, insight')).toBeInTheDocument();
    expect(within(hero).getByRole('heading', { level: 1, name: /See your edge clearly/ })).toBeInTheDocument();
    const features = within(within(hero).getByRole('list')).getAllByRole('listitem');
    expect(features.map((item) => within(item).getByText(/./, { selector: 'b' }).textContent)).toEqual([
      'Learns from every trade',
      'Next-day plan',
      'Portfolio & XIRR',
      'Runs as services',
    ]);

    // The form side: its own heading, labelled fields above the inputs, the footer with the mode toggle.
    expect(screen.getByRole('heading', { level: 2, name: 'Sign in' })).toBeInTheDocument();
    expect(screen.getByLabelText('Email')).toBeInTheDocument();
    expect(screen.getByLabelText('Password')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument();
    expect(screen.getByText('Personal use · single owner')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /mode$/ })).toBeInTheDocument();
  });

  it('shows no system status before sign-in', async () => {
    renderLogin();
    await screen.findByRole('heading', { level: 2, name: 'Sign in' });

    expect(screen.queryByText(/services healthy/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/Next: /)).not.toBeInTheDocument();
    expect(screen.queryByText(/nightly/i)).not.toBeInTheDocument();
  });

  it('links each field to its label and an error to its input', async () => {
    renderLogin();

    await userEvent.click(await screen.findByRole('button', { name: 'Sign in' }));

    const email = screen.getByLabelText('Email');
    const message = screen.getByText('Enter your email address.');
    expect(email).toHaveAttribute('aria-invalid', 'true');
    expect(email).toHaveAccessibleDescription('Enter your email address.');
    expect(message).toBeInTheDocument();
  });

  it('toggles the colour mode from the footer', async () => {
    renderLogin();

    await userEvent.click(await screen.findByRole('button', { name: 'Dark mode' }));

    expect(screen.getByRole('button', { name: 'Light mode' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Light mode' }));
    expect(screen.getByRole('button', { name: 'Dark mode' })).toBeInTheDocument();
  });

  it('collapses to the form card below 860 px, the hero shrinking to its brand row below the form', async () => {
    renderLogin();
    const hero = await screen.findByRole('region', { name: 'About xDrishti' });
    const main = screen.getByRole('main');
    const shell = main.firstElementChild!;

    expect(below860(shell)).toMatch(/grid-template-columns: minmax\(0, 1fr\)/);
    expect(below860(shell)).toMatch(/max-width: 460px/);
    expect(below860(hero)).toMatch(/border-top: 1px solid/);
    // Description and feature list carry the `hero-more` class that the narrow rule hides.
    expect(below860(hero)).toBeTruthy();
    for (const part of [within(hero).getByRole('list'), within(hero).getByText(/self-learning NSE trading desk/)]) {
      expect(part).toHaveClass('hero-more');
    }
  });

  it('keeps the page <h1> at every width: below 860 px the headline is visually hidden, not removed', async () => {
    renderLogin();
    const hero = await screen.findByRole('region', { name: 'About xDrishti' });
    const headline = within(hero).getByRole('heading', { level: 1 });

    expect(headline).not.toHaveClass('hero-more');
    expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1);
    const narrowRules = Array.from(document.styleSheets)
      .flatMap((sheet) => Array.from(sheet.cssRules))
      .filter(
        (rule): rule is CSSMediaRule =>
          'media' in rule && /max-width:\s*860/.test((rule as CSSMediaRule).media.mediaText),
      )
      .flatMap((rule) => Array.from(rule.cssRules))
      .filter((rule): rule is CSSStyleRule => 'selectorText' in rule);
    const forHeadline = narrowRules.filter((rule) => rule.selectorText.includes('.hero-headline'));
    expect(forHeadline.length).toBeGreaterThan(0);
    for (const rule of forHeadline) {
      expect(rule.style.cssText).toMatch(/clip/);
      expect(rule.style.cssText).not.toMatch(/display:\s*none/);
    }
  });
});

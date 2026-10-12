import { screen, within } from '@testing-library/react';
import { createQualityFake } from '@/test/qualityFake';
import { renderQuality } from './testing';

const alert = () => screen.findByRole('region', { name: 'Corporate actions to review' });

describe('Data quality: suggested corporate actions', () => {
  it('says how many splits and bonus issues wait, with a link to their card on the Data screen', async () => {
    renderQuality({ fake: createQualityFake({ pendingCorporateActions: 2 }) });

    const found = await alert();
    expect(found).toHaveTextContent('2 suggested corporate actions to review');
    expect(within(found).getByRole('link', { name: 'Review suggested corporate actions' })).toHaveAttribute(
      'href',
      '/data?section=actions&actions=suggested',
    );
  });

  it('says it in the singular for one', async () => {
    renderQuality({ fake: createQualityFake({ pendingCorporateActions: 1 }) });

    expect(await alert()).toHaveTextContent('1 suggested corporate action to review');
  });

  it('shows nothing when no suggestion waits', async () => {
    renderQuality();

    await screen.findByRole('region', { name: 'Quality summary' });
    expect(screen.queryByRole('region', { name: 'Corporate actions to review' })).not.toBeInTheDocument();
  });

  it('shows nothing while the overview has not been read', async () => {
    renderQuality();

    expect(screen.queryByRole('region', { name: 'Corporate actions to review' })).not.toBeInTheDocument();
    await screen.findByRole('region', { name: 'Quality summary' });
  });
});

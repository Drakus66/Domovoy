import { describe, expect, it } from 'vitest';
import { render, screen } from '../../test/utils';
import CustomDashboardTab from './CustomDashboardTab';
import type { CapabilityDevice } from '../../api/capabilityDevices';
import type { Dashboard } from '../../api/dashboards';

const sensor: CapabilityDevice = {
  id: 'dev-1',
  name: 'Мультисенсор спальня',
  adapterSource: 'zigbee',
  zoneId: '',
  capabilities: [
    { id: 'temperature', kind: 'Number', writable: false, unit: '°C' },
    { id: 'humidity', kind: 'Number', writable: false, unit: '%' },
  ],
  state: { temperature: 21.5, humidity: 47 },
  isOnline: true,
  lastUpdated: '2026-07-06T00:00:00Z',
};

const dashboard = (sections: Dashboard['sections']): Dashboard => ({
  id: 'dash-1', name: 'Климат', icon: null, order: 0, sections,
  createdAt: '2026-07-06T00:00:00Z', updatedAt: '2026-07-06T00:00:00Z',
});

const noop = () => undefined;

describe('CustomDashboardTab', () => {
  it('renders section titles and device tiles', () => {
    render(
      <CustomDashboardTab
        dashboard={dashboard([
          { title: 'Спальня', items: [{ type: 'device', deviceId: 'dev-1' }] },
          { title: 'Кухня', items: [] },
        ])}
        devices={[sensor]}
        onOpen={noop}
        onCommand={noop}
        onEdit={noop}
      />,
    );
    expect(screen.getByText('Спальня')).toBeInTheDocument();
    expect(screen.getByText('Кухня')).toBeInTheDocument();
    expect(screen.getByText('Мультисенсор спальня')).toBeInTheDocument();
    expect(screen.getByText('Здесь пока ничего нет.')).toBeInTheDocument();
  });

  it('renders a capability tile with the formatted value', () => {
    render(
      <CustomDashboardTab
        dashboard={dashboard([
          { title: '', items: [{ type: 'capability', deviceId: 'dev-1', capabilityId: 'humidity' }] },
        ])}
        devices={[sensor]}
        onOpen={noop}
        onCommand={noop}
        onEdit={noop}
      />,
    );
    expect(screen.getByText('47 %')).toBeInTheDocument();
  });

  it('renders the moved-out placeholder for a deleted device instead of crashing', () => {
    render(
      <CustomDashboardTab
        dashboard={dashboard([
          { title: 'Спальня', items: [{ type: 'device', deviceId: 'gone' }] },
        ])}
        devices={[sensor]}
        onOpen={noop}
        onCommand={noop}
        onEdit={noop}
      />,
    );
    expect(screen.getByText(/этот жилец съехал/)).toBeInTheDocument();
  });

  it('shows the house-spirit empty state with an edit button when the tab has no items', () => {
    render(
      <CustomDashboardTab
        dashboard={dashboard([])}
        devices={[sensor]}
        onOpen={noop}
        onCommand={noop}
        onEdit={noop}
      />,
    );
    expect(screen.getByText(/Эта полка пока пуста/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Изменить вкладку' })).toBeInTheDocument();
  });
});

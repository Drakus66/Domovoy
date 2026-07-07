import { useEffect } from 'react';
import { MapContainer, TileLayer, Marker, useMap, useMapEvents } from 'react-leaflet';
import L from 'leaflet';
import 'leaflet/dist/leaflet.css';
import markerIcon2x from 'leaflet/dist/images/marker-icon-2x.png';
import markerIcon from 'leaflet/dist/images/marker-icon.png';
import markerShadow from 'leaflet/dist/images/marker-shadow.png';

// Vite fingerprints the marker PNGs, so Leaflet's default icon paths 404 unless we point them at the
// bundled assets. Do this once at module load.
L.Icon.Default.mergeOptions({
  iconRetinaUrl: markerIcon2x,
  iconUrl: markerIcon,
  shadowUrl: markerShadow,
});

interface LocationMapProps {
  latitude: number;
  longitude: number;
  /** Fired when the user clicks the map or drags the marker to a new spot. */
  onPick: (lat: number, lon: number) => void;
}

// Keep the map centred on the current coordinates when they change from outside (search / manual entry).
function Recenter({ latitude, longitude }: { latitude: number; longitude: number }) {
  const map = useMap();
  useEffect(() => {
    map.setView([latitude, longitude], map.getZoom());
  }, [latitude, longitude, map]);
  return null;
}

function ClickCapture({ onPick }: { onPick: (lat: number, lon: number) => void }) {
  useMapEvents({
    click: (e) => onPick(e.latlng.lat, e.latlng.lng),
  });
  return null;
}

/**
 * Interactive location picker (roadmap Epic 2K). OSM tiles need network, but the map is purely a
 * convenience: clicking or dragging the marker just reports coordinates back to the editor, which also
 * accepts manual lat/lon entry — so an offline install never depends on this component.
 */
export default function LocationMap({ latitude, longitude, onPick }: LocationMapProps) {
  return (
    <MapContainer
      center={[latitude, longitude]}
      zoom={11}
      style={{ height: 320, width: '100%', borderRadius: 8 }}
      scrollWheelZoom
    >
      <TileLayer
        attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
        url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
      />
      <Marker
        position={[latitude, longitude]}
        draggable
        eventHandlers={{
          dragend: (e) => {
            const { lat, lng } = e.target.getLatLng();
            onPick(lat, lng);
          },
        }}
      />
      <Recenter latitude={latitude} longitude={longitude} />
      <ClickCapture onPick={onPick} />
    </MapContainer>
  );
}

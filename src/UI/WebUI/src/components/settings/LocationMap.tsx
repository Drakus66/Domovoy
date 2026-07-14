// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useState } from 'react';
import { Map, Marker } from 'pigeon-maps';

interface LocationMapProps {
  latitude: number;
  longitude: number;
  /** Fired when the user clicks the map to choose a new spot. */
  onPick: (lat: number, lon: number) => void;
}

/**
 * Interactive location picker (roadmap Epic 2K). OSM tiles need network, but the map is purely a
 * convenience: clicking reports coordinates back to the editor, which also accepts manual lat/lon
 * entry — so an offline install never depends on this component.
 *
 * Uses pigeon-maps (MIT, zero-dependency) rather than Leaflet: the previous react-leaflet wrapper
 * shipped under the Hippocratic License, which is not permissive and clashed with the project's
 * AGPL + commercial dual-licensing posture.
 */
export default function LocationMap({ latitude, longitude, onPick }: LocationMapProps) {
  // pigeon-maps is controlled: we own center/zoom so panning and zooming work, and we recenter
  // when the coordinates change from outside (search / manual entry / a fresh pick).
  const [center, setCenter] = useState<[number, number]>([latitude, longitude]);
  const [zoom, setZoom] = useState(11);

  useEffect(() => {
    setCenter([latitude, longitude]);
  }, [latitude, longitude]);

  return (
    <div style={{ height: 320, width: '100%', borderRadius: 8, overflow: 'hidden' }}>
      <Map
        height={320}
        center={center}
        zoom={zoom}
        onBoundsChanged={({ center: c, zoom: z }) => {
          setCenter(c);
          setZoom(z);
        }}
        onClick={({ latLng }) => onPick(latLng[0], latLng[1])}
      >
        <Marker width={40} color="#1976d2" anchor={[latitude, longitude]} />
      </Map>
    </div>
  );
}

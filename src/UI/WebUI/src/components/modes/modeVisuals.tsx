import HomeRoundedIcon from '@mui/icons-material/HomeRounded';
import DirectionsWalkRoundedIcon from '@mui/icons-material/DirectionsWalkRounded';
import BedtimeRoundedIcon from '@mui/icons-material/BedtimeRounded';
import BeachAccessRoundedIcon from '@mui/icons-material/BeachAccessRounded';
import TuneRoundedIcon from '@mui/icons-material/TuneRounded';

/** Icon per well-known mode; unknown custom modes fall back to a generic look.
 *  Shared by the Modes page and the dashboard modes widget. */
export const MODE_ICONS: Record<string, JSX.Element> = {
  Home: <HomeRoundedIcon />,
  Away: <DirectionsWalkRoundedIcon />,
  Night: <BedtimeRoundedIcon />,
  Vacation: <BeachAccessRoundedIcon />,
};

export const modeIcon = (mode: string) => MODE_ICONS[mode] ?? <TuneRoundedIcon />;

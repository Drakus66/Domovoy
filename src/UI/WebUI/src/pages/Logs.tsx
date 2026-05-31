import { Container, Typography, Box } from '@mui/material';

function Logs() {
  return (
    <Container maxWidth="xl">
      <Box sx={{ py: 4 }}>
        <Typography variant="h4" component="h1" gutterBottom>
          System Logs
        </Typography>
        <Typography variant="body1" color="text.secondary">
          Log entries will be displayed here
        </Typography>
      </Box>
    </Container>
  );
}

export default Logs;

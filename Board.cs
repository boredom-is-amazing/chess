using Chess.Utilities;
using Chess.MoveGen;
using Types.Nibble;
using Types.Bitboards;

namespace Chess
{
    public struct PieceSet(Colour colour)
    {
        public Colour colour = colour;

        public Bitboard Bishops = 0;
        public Bitboard Knights = 0;
        public Bitboard Rooks = 0;
        public Bitboard Pawns = 0;
        public Bitboard Queens = 0;
        public Bitboard King = 0;

        public Bitboard Mask { get { return Bishops | Knights | Rooks | Pawns | Queens | King; } }

        public int KingSquare { get {
            if (!King) throw new Exception($"{colour} king bitboard is not present.");
            
            var T = King;
            return T.PopLSB();
        } }

        public Bitboard BaseAttackingBitmask(Bitboard occupancy)
        {
            Bitboard attacks = 0;
            Bitboard T;

            T = Bishops | Queens;
            while (T) attacks |= Moves.GetBishopMoveBitmask(occupancy, T.PopLSB());

            T = Rooks | Queens;
            while (T) attacks |= Moves.GetRookMoveBitmask(occupancy, T.PopLSB());

            T = Knights;
            while (T) attacks |= Moves.fmt.KNIGHT_MOVES_TABLE[T.PopLSB()];

            attacks |= Moves.fmt.KING_MOVES_TABLE[KingSquare]; // Get king moves from table

            Bitboard pawnLeftAttacks;
            Bitboard pawnRightAttacks;

            if (colour == Colour.White)
            {
                pawnLeftAttacks  = (Pawns & ~Files.A) << 7;
                pawnRightAttacks = (Pawns & ~Files.H) << 9;
            }
            else
            {
                pawnLeftAttacks  = (Pawns & ~Files.A) >> 9;
                pawnRightAttacks = (Pawns & ~Files.H) >> 7;
            }

            attacks |= pawnLeftAttacks | pawnRightAttacks;

            return attacks;
        }

        public Bitboard AttackingBitmask(Bitboard opponentOccupancy)
        {
            return BaseAttackingBitmask(Mask | opponentOccupancy) & ~Mask;
        }

        public Bitboard ProtectedBitmask(Bitboard opponentOccupancy)
        {
            return BaseAttackingBitmask(Mask | opponentOccupancy) & Mask & ~King; // cannot protect king
        }
    }

    public enum Piece
    {
        WhitePawn,
        BlackPawn,
        WhiteKnight,
        BlackKnight,
        WhiteBishop,
        BlackBishop,
        WhiteRook,
        BlackRook,
        WhiteQueen,
        BlackQueen,
        WhiteKing,
        BlackKing,
        Empty
    }

    public class BoardInfo
    {
        public Bitboard EPsquare;
        public uint halfMoveClock;
        public Nibble castlingRights;
        public Piece capturedPiece = Piece.Empty;
        
        public Bitboard checkmask;
        public Bitboard pinD;
        public Bitboard pinHV;
    }

    public class Board
    {
        public Piece[] BoardArray;
        public PieceSet White;
        public PieceSet Black;
        public int moveCounter;
        public uint halfMoveClock;
        public Nibble castlingRights; // 0b1111 => KQkq
        public Stack<Move> moveHistory = new();
        public Stack<BoardInfo> boardHistory = new();

        public Bitboard pinHV;
        public Bitboard pinD;
        public Bitboard checkmask;
        public Bitboard checkers;

        public Bitboard boardMask { get { return White.Mask | Black.Mask; } }
        public string FEN { get { return GetFEN(); } }
        
        public int SideToMove { get { return moveCounter & 1; } }
        public Colour ColourToMove { get { return (Colour)SideToMove; } }
        public PieceSet PlayerToMove { get { return SideToMove == 0 ? White : Black; } }
        public PieceSet OpponentToMove { get { return SideToMove == 0 ? Black : White; }}

        public Bitboard epSquare;

        public const int whiteStartingKingSquare = 4;
        public const int blackStartingKingSquare = 60;

        public Board(string FEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")
        {            
            // Create piece sets for each side
            White = new(Colour.White);
            Black = new(Colour.Black);

            // Split the FEN string into sections
            string[] FENstringSections = FEN.Split(' ');

            // Assign variables to each of the sections
            string boardFEN           =  FENstringSections[0];
            string sideToMoveString   =  FENstringSections[1];
            string FENcastlingRights  =  FENstringSections[2];
            string epSquareString     =  FENstringSections[3];
            string halftimeCounter    =  FENstringSections[4];
            string currentDoubleMove  =  FENstringSections[5];

            // Reverse FEN string to have a reversed board format
            boardFEN = string.Join("/", boardFEN.Split('/').Reverse());
            
            // Fill the board with pieces based on the FEN string
            BoardArray = new Piece[64];
            Array.Fill(BoardArray, Piece.Empty);

            int cursor = 0;

            // Fill the board with ecah character
            foreach (char c in boardFEN)
            {
                // Skip these because we don't use a 2D array
                if (c == '/') continue;

                // If the current character is a number (eg. N)
                if (int.TryParse(c.ToString(), out int emptySpaces))
                {
                    // Add N empty spaces to the 
                    for (int i = 0; i < emptySpaces; i++)
                    {
                        BoardArray[cursor++] = Piece.Empty;
                    }

                    // cursor += emptySpaces; // Keep cursor updated
                    continue;
                }

                // Pattern match each character in the FEN string to its
                // relative Piece enum to be inserted into the board.
                BoardArray[cursor] = c switch
                {
                    'b' => Piece.BlackBishop,
                    'n' => Piece.BlackKnight,
                    'r' => Piece.BlackRook,
                    'q' => Piece.BlackQueen,
                    'p' => Piece.BlackPawn,
                    'k' => Piece.BlackKing,

                    'B' => Piece.WhiteBishop,
                    'N' => Piece.WhiteKnight,
                    'R' => Piece.WhiteRook,
                    'Q' => Piece.WhiteQueen,
                    'K' => Piece.WhiteKing,
                    'P' => Piece.WhitePawn,

                    _ => throw new Exception($"invalid character '{c}' found in FEN string.")
                };

                // Pattern match each character in the FEN string to the bitboard
                // to insert a bit into, using the cursor argument.
                var _ = c switch
                {
                    'b' => Black.Bishops |= 1UL << cursor,
                    'n' => Black.Knights |= 1UL << cursor,
                    'r' => Black.Rooks   |= 1UL << cursor,
                    'q' => Black.Queens  |= 1UL << cursor,
                    'k' => Black.King    |= 1UL << cursor,
                    'p' => Black.Pawns   |= 1UL << cursor,

                    'B' => White.Bishops |= 1UL << cursor,
                    'N' => White.Knights |= 1UL << cursor,
                    'R' => White.Rooks   |= 1UL << cursor,
                    'Q' => White.Queens  |= 1UL << cursor,
                    'K' => White.King    |= 1UL << cursor,
                    'P' => White.Pawns   |= 1UL << cursor,

                    _ => throw new Exception($"invalid character '{c}' found in FEN string.")
                };

                // Increase the cursor to the new position
                cursor++;
            }

            // Set move counter based on side to move and current double move
            int sideToMoveIncrease = sideToMoveString switch
            {
                "w" => 0, "b" => 1,
                _ => throw new Exception($"invalid side to move character.")
            };

            if (!int.TryParse(currentDoubleMove, out int currentDoubleMoveNumber))
            {
                throw new Exception($"invalid current move character.");
            }

            moveCounter = currentDoubleMoveNumber * 2 + sideToMoveIncrease;

            // Set castling rights
            castlingRights = 0;

            if (FENcastlingRights != "-")
            {
                foreach (char castlingRight in FENcastlingRights)
                {
                    castlingRights |= castlingRight switch {
                        'K' => 0b1000,
                        'Q' => 0b0100,
                        'k' => 0b0010,
                        'q' => 0b0001,
                        _ => throw new Exception("invalid castling rights.")
                    };
                }
            }

            // Set the halftime counter
            if (!uint.TryParse(halftimeCounter, out uint HTC))
            {
                throw new Exception("half time counter was not a positive integer.");
            }

            halfMoveClock = HTC;

            // Set an en-passant square
            if (epSquareString != "-")
            {
                int rank = "12345678".IndexOf(epSquareString[1]);
                int file = "abcdefgh".IndexOf(epSquareString[0]);

                int epSquareIndex = rank * 8 + file;

                epSquare = 1UL << epSquareIndex;
            }

            UpdatePinsAndCheckers();
        }


        public Bitboard GetBitboardFromEnum(Piece pieceEnum)
        {
            Move getLastMovePlayed()
            {
                var T = moveHistory.Pop();
                moveHistory.Push(T);
                return T;
            }

            return pieceEnum switch
            {
                Piece.BlackBishop =>  Black.Bishops,
                Piece.BlackKnight =>  Black.Knights,
                Piece.BlackKing   =>  Black.King,
                Piece.BlackQueen  =>  Black.Queens,
                Piece.BlackRook   =>  Black.Rooks,
                Piece.BlackPawn   =>  Black.Pawns,

                Piece.WhiteBishop =>  White.Bishops,
                Piece.WhiteKnight =>  White.Knights,
                Piece.WhitePawn   =>  White.Pawns,
                Piece.WhiteQueen  =>  White.Queens,
                Piece.WhiteRook   =>  White.Rooks,
                Piece.WhiteKing   =>  White.King,

                Piece.Empty => throw new Exception($"cannot obtain bitboard for an empty piece on move {getLastMovePlayed()}.\nBoard:\n{this}\n\nMove history: [{string.Join(", ", moveHistory.Reverse())}]\n\nPerhaps your start square is wrong?\n"),

                _ => throw new Exception($"cannot set bitboard for unaccounted enum \"{pieceEnum}\".") // raised errors if not in place
            };
        }

        public void SetBitboardFromEnum(Piece pieceEnum, Bitboard bitboard)
        {
            var _ = pieceEnum switch
            {
                Piece.BlackBishop =>  Black.Bishops = bitboard,
                Piece.BlackKnight =>  Black.Knights = bitboard,
                Piece.BlackKing   =>  Black.King = bitboard,
                Piece.BlackQueen  =>  Black.Queens = bitboard,
                Piece.BlackRook   =>  Black.Rooks = bitboard,
                Piece.BlackPawn   =>  Black.Pawns = bitboard,

                Piece.WhiteBishop =>  White.Bishops = bitboard,
                Piece.WhiteKnight =>  White.Knights = bitboard,
                Piece.WhitePawn   =>  White.Pawns = bitboard,
                Piece.WhiteQueen  =>  White.Queens = bitboard,
                Piece.WhiteRook   =>  White.Rooks = bitboard,
                Piece.WhiteKing   =>  White.King = bitboard,

                Piece.Empty => throw new Exception($"cannot set bitboard for an empty piece enum."),
                
                _ => throw new Exception($"cannot set bitboard for unaccounted enum \"{pieceEnum}\".") // raised errors if not in place
            };
        }

        public void MakeMove(Move move)
        {
            // Archive move
            moveHistory.Push(move);
            moveCounter++;

            Piece pieceCaptured;

            // Board info for archiving
            BoardInfo boardInfo = new()
            {
                EPsquare = epSquare,
                castlingRights = castlingRights,
                checkmask = checkmask,
                pinHV = pinHV,
                pinD = pinD
            };

            // Modify the board based on the type of move played
            switch (move.type)
            {
                case MoveType.Normal:
                    Piece pieceToMove = BoardArray[move.src];
                    
                    // Handle disabling castling rights when moving kings or rooks
                    switch (pieceToMove)
                    {
                        case Piece.WhiteKing: // Remove white castling rights
                            castlingRights &= ~0b1100;
                            break;
                        
                        case Piece.BlackKing:
                            castlingRights &= ~0b0011;
                            
                            break;

                        case Piece.WhiteRook:
                            if (move.src == 0) castlingRights &= ~0b0100; // starting queenside rook position
                            if (move.src == 7) castlingRights &= ~0b1000; // starting kingside rook position

                            break;
                        
                        case Piece.BlackRook:
                            if (move.src == 56) castlingRights &= ~0b0001; // starting queenside rook position
                            if (move.src == 63) castlingRights &= ~0b0010; // starting kingside rook position
                            
                            break;

                        default:
                            break;
                    }
                    
                    // Update piece bitboard
                    Bitboard bb = GetBitboardFromEnum(pieceToMove);

                    bb ^= 1UL << move.src | 1UL << move.dst;
                    SetBitboardFromEnum(pieceToMove, bb);

                    // Check if piece was a capture
                    if (BoardArray[move.dst] != Piece.Empty)
                    {
                        // Get the piece that was captured
                        pieceCaptured = BoardArray[move.dst];

                        // If the piece captured was a rook, remove castling rights
                        // for whatever side it was taken from
                        if (pieceCaptured == Piece.WhiteRook)
                        {
                            if (move.dst == 0) castlingRights &= ~0b0100;
                            if (move.dst == 7) castlingRights &= ~0b1000;
                        }

                        if (pieceCaptured == Piece.BlackRook)
                        {
                            if (move.dst == 56) castlingRights &= ~0b0001;
                            if (move.dst == 63) castlingRights &= ~0b0010;
                        }

                        // Update the captured piece's bitboard
                        bb = GetBitboardFromEnum(pieceCaptured);
                        bb ^= 1UL << move.dst;
                        SetBitboardFromEnum(pieceCaptured, bb);

                        boardInfo.capturedPiece = pieceCaptured;
                    }

                    // Update board array
                    BoardArray[move.src] = Piece.Empty;
                    BoardArray[move.dst] = pieceToMove;

                    // Clear en-passant square
                    epSquare = 0;

                    break;

                case MoveType.EnPassant:
                    pieceToMove = BoardArray[move.src];

                    // Update bitboard of piece
                    bb = GetBitboardFromEnum(pieceToMove);
                    bb ^= 1UL << move.src | 1UL << move.dst;
                    SetBitboardFromEnum(pieceToMove, bb);

                    // Get square of pawn to capture
                    int inFrontOfEPsquare = move.dst - 8 * (move.src < move.dst ? 1 : -1);
                    
                    Piece opponentPawnType = BoardArray[inFrontOfEPsquare];
                    
                    // Remove pawn from bitboard and update it
                    Bitboard opponentPawnBB = GetBitboardFromEnum(opponentPawnType);

                    opponentPawnBB ^= 1UL << inFrontOfEPsquare;
                    SetBitboardFromEnum(opponentPawnType, opponentPawnBB);

                    // Remove pawn from board array
                    BoardArray[move.src] = Piece.Empty;
                    BoardArray[move.dst] = pieceToMove;
                    BoardArray[inFrontOfEPsquare] = Piece.Empty;

                    // Clear EP square (already been used)
                    epSquare = 0;

                    break;
                
                case MoveType.PawnDoublePush:
                    pieceToMove = BoardArray[move.src];

                    // Get the square behind the pawn by getting the
                    // middle square between the start and end of the
                    // double pawn push.
                    int newEPsquare = (move.src + move.dst) / 2;
                    epSquare = 1UL << newEPsquare;
                    
                    // Update piece bitboard
                    bb = GetBitboardFromEnum(pieceToMove);
                    bb ^= 1UL << move.src | 1UL << move.dst;
                    SetBitboardFromEnum(pieceToMove, bb);

                    // Update board array
                    BoardArray[move.src] = Piece.Empty;
                    BoardArray[move.dst] = pieceToMove;

                    break;
                
                case MoveType.Castling:
                    // Clear en-passant square
                    epSquare = 0;

                    // Get the king moving
                    Piece kingToMove = Piece.BlackKing - SideToMove;
                    
                    // Reset castling rights depending on side
                    castlingRights &= ~(0b0011 << 2 * SideToMove);

                    // Update bitboard of king
                    bb = GetBitboardFromEnum(kingToMove);
                    bb ^= 1UL << move.src | 1UL << move.dst;
                    SetBitboardFromEnum(kingToMove, bb);

                    // Get position of rook to castle with on the board
                    int rookPosition = move.dst switch
                    {
                        2 => 0,    // from C1 to A1
                        6 => 7,    // from G1 to H1

                        58 => 56,  // from C8 to A8
                        62 => 63,  // from G8 to H8
                        
                        _ => throw new Exception($"invalid castling destination square: {move.dst} (Move: {move})")
                    };

                    // Rook will always end up between the king's start and end square
                    int endRookPosition = (move.src + move.dst) / 2;

                    // Get rook enum (used for obtaining bitboard)
                    Piece rookEnum = Piece.BlackRook - SideToMove;
                    
                    // Update bitboard of rook
                    Bitboard rookBB = GetBitboardFromEnum(rookEnum);
                    rookBB ^= 1UL << rookPosition | 1UL << endRookPosition;
                    SetBitboardFromEnum(rookEnum, rookBB);

                    // Update array
                    BoardArray[move.src] = Piece.Empty;
                    BoardArray[rookPosition] = Piece.Empty;
                    
                    BoardArray[move.dst] = kingToMove;
                    BoardArray[endRookPosition] = rookEnum;

                    break;
                
                case MoveType.Promotion:
                    // Clear EP square
                    epSquare = 0;

                    pieceToMove = BoardArray[move.src];

                    // Move piece on array
                    BoardArray[move.src] = Piece.Empty;
                    
                    Piece promotedPiece = move.promoPiece switch
                    {
                        PromoPiece.Bishop => Piece.BlackBishop - SideToMove,
                        PromoPiece.Knight => Piece.BlackKnight - SideToMove,
                        PromoPiece.Rook   => Piece.BlackRook   - SideToMove,
                        PromoPiece.Queen  => Piece.BlackQueen  - SideToMove,
                        _ => throw new Exception($"promotion piece \"{move.promoPiece}\" unaccounted for.")
                    };

                    Piece pieceLandedOn = BoardArray[move.dst];

                    if (pieceLandedOn != Piece.Empty)
                    {
                        boardInfo.capturedPiece = pieceLandedOn;

                        bb = GetBitboardFromEnum(pieceLandedOn);
                        bb ^= 1UL << move.dst;
                        SetBitboardFromEnum(pieceLandedOn, bb);
                    }

                    pieceCaptured = BoardArray[move.dst];

                    BoardArray[move.dst] = promotedPiece;

                    if (pieceCaptured == Piece.WhiteRook)
                    {
                        if (move.dst == 0) castlingRights &= ~0b0100;
                        if (move.dst == 7) castlingRights &= ~0b1000;
                    }

                    if (pieceCaptured == Piece.BlackRook)
                    {
                        if (move.dst == 56) castlingRights &= ~0b0001;
                        if (move.dst == 63) castlingRights &= ~0b0010; // issue somewhere here with castling and shit
                    }

                    // Update both sets of bitboards
                    bb = GetBitboardFromEnum(pieceToMove);
                    bb ^= 1UL << move.src;
                    SetBitboardFromEnum(pieceToMove, bb);

                    bb = GetBitboardFromEnum(promotedPiece);
                    bb ^= 1UL << move.dst;
                    SetBitboardFromEnum(promotedPiece, bb);

                    break;
                
                default: // move flag was unaccounted for
                    throw new Exception($"move flag \"{move.type}\" on move {move} unaccounted for.");
            }

            // If the move played was a promotion move (pawn was promoted) or a piece
            // on the board was captured, reset the halfmove clock
            if (move.type == MoveType.Promotion || boardInfo.capturedPiece != Piece.Empty) halfMoveClock = 0;

            // Otherwise, increase the halfmove clock
            else halfMoveClock++;
            
            boardHistory.Push(boardInfo);
            
            UpdatePinsAndCheckers();

            CheckForRepetitions();
        }

        public void UndoMove()
        {
            if (moveHistory.Count == 0) throw new Exception("cannot undo when no moves on the board have been played.");

            // Get last move and last board info
            Move previousMove = moveHistory.Pop();
            BoardInfo previousBoardInfo = boardHistory.Pop();

            checkmask       =  previousBoardInfo.checkmask;
            pinD            =  previousBoardInfo.pinD;
            pinHV           =  previousBoardInfo.pinHV;
            castlingRights  =  previousBoardInfo.castlingRights;
            epSquare        =  previousBoardInfo.EPsquare;
            halfMoveClock   =  previousBoardInfo.halfMoveClock;
            
            moveCounter--; // Decrease move counter

            Piece pieceThatMoved = BoardArray[previousMove.dst];

            // Edit board based on type of previous move
            switch (previousMove.type)
            {
                case MoveType.Normal:
                    // Update bitboard of piece
                    Bitboard bb = GetBitboardFromEnum(pieceThatMoved);

                    bb ^= 1UL << previousMove.dst | 1UL << previousMove.src;
                    SetBitboardFromEnum(pieceThatMoved, bb);

                    // Check if a piece was captured
                    // If so, update their bitboard as well
                    if (previousBoardInfo.capturedPiece != Piece.Empty)
                    {
                        bb = GetBitboardFromEnum(previousBoardInfo.capturedPiece);
                        bb ^= 1UL << previousMove.dst;
                        SetBitboardFromEnum(previousBoardInfo.capturedPiece, bb);
                    }

                    BoardArray[previousMove.dst] = previousBoardInfo.capturedPiece;
                    BoardArray[previousMove.src] = pieceThatMoved;

                    break;

                case MoveType.EnPassant:
                    // Get whichever pawn type did the en-passant
                    Piece pawnType = Piece.WhitePawn + SideToMove;
                    
                    // Update bitboard of that piece
                    bb = GetBitboardFromEnum(pawnType);
                    bb ^= 1UL << previousMove.dst | 1UL << previousMove.src;
                    SetBitboardFromEnum(pawnType, bb);

                    Piece opponentPawnType = Piece.BlackPawn - SideToMove;

                    // Get the square in front of the EP square (relative to the side moving)
                    int squarePawnWasTakenFrom = previousMove.dst + (SideToMove == 0 ? -8 : 8);

                    // Update bitboard of opponent pawn type
                    // (replace the pawn that was captured)
                    bb = GetBitboardFromEnum(opponentPawnType);
                    bb ^= 1UL << squarePawnWasTakenFrom;
                    SetBitboardFromEnum(opponentPawnType, bb);

                    // Update board array
                    BoardArray[previousMove.src] = pawnType;
                    BoardArray[previousMove.dst] = Piece.Empty;
                    BoardArray[squarePawnWasTakenFrom] = opponentPawnType;

                    // En-passant square is previous move destination
                    epSquare = previousBoardInfo.EPsquare;

                    break;

                case MoveType.PawnDoublePush:
                    pawnType = BoardArray[previousMove.dst];

                    bb = GetBitboardFromEnum(pawnType);
                    bb ^= 1UL << previousMove.dst | 1UL << previousMove.src;
                    SetBitboardFromEnum(pawnType, bb);

                    // Reset en-passant square
                    epSquare = previousBoardInfo.EPsquare;

                    // Update board array
                    BoardArray[previousMove.src] = pawnType;
                    BoardArray[previousMove.dst] = Piece.Empty;

                    break;

                case MoveType.Castling:
                    Piece kingEnum = Piece.WhiteKing + SideToMove;

                    // Update king bitboard
                    bb = GetBitboardFromEnum(kingEnum);
                    bb ^= 1UL << previousMove.dst | 1UL << previousMove.src;
                    SetBitboardFromEnum(kingEnum, bb);

                    // Get rook position
                    // Rook position is always between the king's source
                    // and the king's destination square
                    int rookPosition = (previousMove.src + previousMove.dst) / 2;

                    // Get the end rook position:
                    /*
                       1 => start pos
                       2 => end pos

                       2 . . 1 . 1 . 2
                       . . . . . . . .
                       . . . . . . . .
                       . . . . . . . .
                       . . . . . . . .
                       . . . . . . . .
                       . . . . . . . .
                       2 . . 1 . 1 . 2
                    */
                    int endRookPosition = rookPosition switch
                    {
                        3 => 0, // from D1 to A1
                        5 => 7, // from F1 to H1

                        59 => 56, // from D8 to A8
                        61 => 63, // from F8 to H8

                        _ => throw new Exception("invalid rook position.")
                    };
                    
                    Piece rookEnum = Piece.WhiteRook + SideToMove;

                    // Update rook bitboard
                    bb = GetBitboardFromEnum(rookEnum);
                    bb ^= 1UL << rookPosition | 1UL << endRookPosition;
                    SetBitboardFromEnum(rookEnum, bb);

                    // Reset castling rights
                    castlingRights = previousBoardInfo.castlingRights;

                    // Update board array
                    BoardArray[previousMove.dst] = Piece.Empty;
                    BoardArray[previousMove.src] = kingEnum;

                    BoardArray[rookPosition] = Piece.Empty;
                    BoardArray[endRookPosition] = rookEnum;

                    // Reset en-passant square
                    epSquare = previousBoardInfo.EPsquare;

                    break;

                case MoveType.Promotion:
                    // Reset en-passant square
                    epSquare = previousBoardInfo.EPsquare;

                    Piece promotedPiece = previousMove.promoPiece switch
                    {
                        PromoPiece.Bishop => Piece.WhiteBishop + SideToMove,
                        PromoPiece.Knight => Piece.WhiteKnight + SideToMove,
                        PromoPiece.Rook   => Piece.WhiteRook   + SideToMove,
                        PromoPiece.Queen  => Piece.WhiteQueen  + SideToMove,
                        _ => throw new Exception($"promotion piece \"{previousMove.promoPiece}\" unaccounted for.")
                    };

                    pawnType = Piece.WhitePawn + SideToMove;

                    if (previousBoardInfo.capturedPiece != Piece.Empty)
                    {
                        bb = GetBitboardFromEnum(previousBoardInfo.capturedPiece);
                        bb ^= 1UL << previousMove.dst;
                        SetBitboardFromEnum(previousBoardInfo.capturedPiece, bb);
                    }

                    // Update board array
                    BoardArray[previousMove.src] = pawnType;
                    BoardArray[previousMove.dst] = previousBoardInfo.capturedPiece;

                    // Update bitboards
                    bb = GetBitboardFromEnum(pawnType);
                    bb ^= 1UL << previousMove.src;
                    SetBitboardFromEnum(pawnType, bb);

                    bb = GetBitboardFromEnum(promotedPiece);
                    bb ^= 1UL << previousMove.dst;
                    SetBitboardFromEnum(promotedPiece, bb);

                    break;

                default:
                    throw new Exception($"move flag \"{previousMove.type}\" on move {previousMove} unaccounted for.");
            }

            UpdatePinsAndCheckers();
        }

        public List<Move> GenerateLegalMoves()
        {
            List<Move> moves = [];

            // If there are two checkers, only generate king moves
            if (checkers.BitCount() == 2)
            {
                Moves.GenerateKingMoves(
                    friendlyPieces: PlayerToMove,
                    opponentPieces: OpponentToMove,
                    square: PlayerToMove.KingSquare,
                    moveListToAddTo: moves
                );
                
                return moves;
            }

            Bitboard rooksQueens = PlayerToMove.Queens | PlayerToMove.Rooks;

            while (rooksQueens)
            {
                Moves.GenerateRookMoves(
                    friendlyOccupancy: PlayerToMove.Mask,
                    opponentOccupancy: OpponentToMove.Mask,
                    square: rooksQueens.PopLSB(),
                    pinHV: pinHV,
                    pinD: pinD,
                    checkmask: checkmask,
                    moveListToAddTo: moves
                );
            }

            Bitboard bishopsQueens = PlayerToMove.Bishops | PlayerToMove.Queens;

            while (bishopsQueens)
            {
                Moves.GenerateBishopMoves(
                    friendlyOccupancy: PlayerToMove.Mask,
                    opponentOccupancy: OpponentToMove.Mask,
                    square: bishopsQueens.PopLSB(),
                    pinHV: pinHV,
                    pinD: pinD,
                    checkmask: checkmask,
                    moveListToAddTo: moves
                );
            }

            Bitboard knights = PlayerToMove.Knights;

            while (knights)
            {
                Moves.GenerateKnightMoves(
                    friendlyOccupancy: PlayerToMove.Mask,
                    square: knights.PopLSB(),
                    moveListToAddTo: moves,
                    pinmask: pinD | pinHV,
                    checkmask: checkmask
                );
            }

            Moves.GeneratePawnMoves(
                whitePieces: White,
                blackPieces: Black,
                epBitboard: epSquare,
                moveList: moves,
                pinHV: pinHV,
                pinD: pinD,
                checkmask: checkmask,
                side: ColourToMove
            );

            Moves.GenerateKingMoves(
                friendlyPieces: PlayerToMove,
                opponentPieces: OpponentToMove,
                square: PlayerToMove.KingSquare,
                moveListToAddTo: moves
            );

            if (checkers.BitCount() == 0) // not in check
            {
                Moves.GenerateCastlingMoves(
                    sideToMove: ColourToMove,
                    friendlyPieces: PlayerToMove,
                    opponentPieces: OpponentToMove,
                    castlingRights: castlingRights,
                    moveListToAddTo: moves
                );
            }

            return moves;
        }


        static Bitboard RayBetween(int square1, int square2)
        {
            int file1, rank1, file2, rank2;

            file1 = square1 % 8;
            rank1 = square1 / 8;

            file2 = square2 % 8;
            rank2 = square2 / 8;

            if (file1 == file2 || rank1 == rank2) // same row or file
            {
                return Moves.GetRookMoveBitmask(1UL << square2, square1)
                     & Moves.GetRookMoveBitmask(1UL << square1, square2);
            }

            if (Math.Abs(file1 - file2) == Math.Abs(rank1 - rank2))
            {
                return Moves.GetBishopMoveBitmask(1UL << square2, square1)
                     & Moves.GetBishopMoveBitmask(1UL << square1, square2);
            }

            throw new Exception($"cannot form ray between squares \"{square1}\" and \"{square2}\" becuase they are not on the same line.");
        }


        public void UpdatePinsAndCheckers()
        {
            PieceSet us = PlayerToMove;
            PieceSet them = OpponentToMove;

            Bitboard knightCheckers = Moves.fmt.KNIGHT_MOVES_TABLE[us.KingSquare] & them.Knights;

            // Set directions for generating pawn checkers
            Direction upLeft, upRight;

            if (us.colour == Colour.White)
            {
                upLeft  = Direction.Northwest;
                upRight = Direction.Northeast;
            }
            else
            {
                upLeft  = Direction.Southeast;
                upRight = Direction.Southwest;
            }

            // Manually generate pawn checkers instead of using a function because the function
            // accounts for taking pieces instead of simply attacking squares, meaning we can't
            // use it for this purpose.
            Bitboard pawnCheckers = them.Pawns & (us.King.Shift(upLeft) | us.King.Shift(upRight));

            // For rooks, queens and bishops
            Bitboard queens = GetBitboardFromEnum(Piece.BlackQueen - SideToMove);

            Bitboard bishopsQueens = GetBitboardFromEnum(Piece.BlackBishop - SideToMove) ^ queens;
            Bitboard rooksQueens   = GetBitboardFromEnum(Piece.BlackRook   - SideToMove) ^ queens;

            Bitboard bishopAttacks = bishopsQueens & Moves.GetBishopMoveBitmask(them.Mask, us.KingSquare);
            Bitboard rookAttacks = rooksQueens & Moves.GetRookMoveBitmask(them.Mask, us.KingSquare);
            
            checkers = pawnCheckers | knightCheckers;
            checkmask = pawnCheckers | knightCheckers;
            
            pinD = 0;
            pinHV = 0;
            
            while (bishopAttacks)
            {
                int sq = bishopAttacks.PopLSB();

                Bitboard checkray = RayBetween(us.KingSquare, sq);
                Bitboard blockers = checkray & us.Mask;
                int numBlockers = blockers.BitCount();

                if (numBlockers == 0)
                {
                    checkmask |= checkray | 1UL << sq;
                    checkers |= 1UL << sq;
                }
                else if (numBlockers == 1)
                {
                    pinD |= checkray | 1UL << sq | blockers;
                }
            }

            while (rookAttacks)
            {
                int sq = rookAttacks.PopLSB();

                Bitboard checkray = RayBetween(us.KingSquare, sq);
                Bitboard blockers = checkray & us.Mask;
                int numBlockers = blockers.BitCount();

                if (numBlockers == 0)
                {
                    checkmask |= checkray | 1UL << sq;
                    checkers |= 1UL << sq;
                }
                else if (numBlockers == 1)
                {
                    pinHV |= checkray | 1UL << sq | blockers;
                }
            }

            if (!checkmask) checkmask = Bitboard.Filled;
        }


        public override string ToString()
        {
            string[] board = new string[8];

            for (int rank = 0; rank < 8; rank++)
            {
                string line = "";

                for (int file = 0; file < 8; file++)
                {
                    int index = rank * 8 + file;

                    char stringPiece = BoardArray[index] switch
                    {
                        Piece.WhitePawn => 'P',
                        Piece.WhiteBishop => 'B',
                        Piece.WhiteKnight => 'N',
                        Piece.WhiteQueen => 'Q',
                        Piece.WhiteRook => 'R',
                        Piece.WhiteKing => 'K',

                        Piece.BlackPawn => 'p',
                        Piece.BlackBishop => 'b',
                        Piece.BlackKnight => 'n',
                        Piece.BlackRook => 'r',
                        Piece.BlackQueen => 'q',
                        Piece.BlackKing => 'k',

                        _ => '.'
                    };

                    line += stringPiece + " ";
                }

                board[7 - rank] = line;
            }

            return string.Join("\n", board);
        }

        public void CheckForRepetitions()
        {
            Dictionary<Move, int> pastMoves = new();

            foreach (Move pastMove in moveHistory)
            {
                pastMoves[pastMove] = pastMoves.ContainsKey(pastMove) ? pastMoves[pastMove] + 1 : 1;

                if (pastMoves.ContainsValue(3))
                {
                    throw new Exception($"repeated position 3 times. Move that triggered this: {pastMove}");
                }
            }
        }

        private string GetFEN()
        {
            string[] linesOfFEN = new string[8];
            int emptySpaces = 0;

            // Encode the board into a FEN string
            for (int rank = 7; rank != -1; rank--)
            {
                string line = "";

                for (int file = 0; file < 8; file++)
                {
                    int index = rank * 8 + file;

                    if (BoardArray[index] == Piece.Empty)
                    {
                        emptySpaces++;
                    }
                    else
                    {
                        if (emptySpaces > 0)
                        {
                            line += $"{emptySpaces}";
                            emptySpaces = 0;
                        }
                        
                        line += BoardArray[index] switch
                        {
                            Piece.WhiteBishop => 'B',
                            Piece.BlackBishop => 'b',
                            Piece.WhiteKnight => 'N',
                            Piece.BlackKnight => 'n',
                            Piece.WhiteRook   => 'R',
                            Piece.BlackRook   => 'r',
                            Piece.WhitePawn   => 'P',
                            Piece.BlackPawn   => 'p',
                            Piece.WhiteQueen  => 'Q',
                            Piece.BlackQueen  => 'q',
                            Piece.WhiteKing   => 'K',
                            Piece.BlackKing   => 'k',
                            
                            _ => throw new Exception($"piece enum {BoardArray[index]} unaccounted for while constructing FEN string.")
                        };
                    }
                }

                if (emptySpaces > 0)
                {
                    line += $"{emptySpaces}";
                    emptySpaces = 0;
                }

                linesOfFEN[rank] = line;
            }

            string FEN = string.Join("/", linesOfFEN.Reverse());

            // Add the side to move
            string[] sidesToMove = ["w", "b"];
            FEN += " " + sidesToMove[SideToMove];

            string castlingRightsToAdd;

            // Translate the castling rights from a int to a string
            if (castlingRights == 0)
            {
                castlingRightsToAdd = "-";
            }
            else
            {
                castlingRightsToAdd = "";
                string[] castlingRightsValues = ["q", "k", "Q", "K"];

                // Go back to front (start at MSB instead of LSB)
                for (int x = 3; x > -1; x--)
                {
                    if ((castlingRights & (1 << x)) != 0)
                    {
                        castlingRightsToAdd += castlingRightsValues[x];
                    }
                }
            }

            // Add the castling rights
            FEN += " " + castlingRightsToAdd;

            // Add the en-passant square
            if (epSquare == 0)
            {
                FEN += " -";
            }
            else
            {
                int sq = epSquare.PopLSB();
                string sqName = $"{"abcedfgh"[sq % 8]}{"12345678"[sq / 8]}";
                FEN += " " + sqName;
            }

            // Add halftime move counter
            FEN += $" {halfMoveClock}";

            // Add fulltime move counter
            FEN += " " + Convert.ToString((moveCounter - SideToMove) / 2);
            
            return FEN;
        }
    }
}
﻿using System;
using System.Text;
using System.Windows.Forms;

namespace DevPython
{
    class Tokenizer
    {
        #region PRIVATE_VARIABLES
        String _buf;
        int buf;
        int cur;
        int inp;
        int start;
        int end;
        int line_start;
        int done;
        int indent;
        int[] indstack;
        bool atbol;
        int pendin;
        int lineno;
        int level;
        int cont_line;
        #endregion

        public Tokenizer(String input)
        {
            _buf = input;
            // buf = cur = end = inp = 0;
            done = E_OK;
            indstack = new int[64]; // indent stack size
            atbol = true;
        }

        public bool ok()
        {
            return done == E_OK;
        }

        public int get(out String p)
        {
            nextline:
            p = "";
            int c;
            bool blankline = false, nonascii = false;
            start = 0;

            /* Get indentation level */
            if (atbol)
            {
                int col = 0;
                atbol = false;
                for (;;)
                {
                    c = nextc();
                    if (c == ' ')
                        col++;
                    else if (c == '\t')
                        col += 4;
                    else break;
                }
                backup(c);
                if (c == '#' || c == '\n')
                {
                    /* Lines with only whitespace and/or comments
                       shouldn't affect the indentation and are
                       not passed to the parser as NEWLINE tokens,
                       except *totally* empty lines in interactive
                       mode, which signal the end of a command group. */
                    if (col == 0 && c == '\n')
                    {
                        blankline = false; /* Let it through */
                    }
                    else
                    {
                        blankline = true; /* Ignore completely */
                    }
                    /* We can't jump back right here since we still
                       may need to skip to the end of a comment */
                }
                if (!blankline && level == 0)
                {
                    if (col == indstack[indent])
                    {
                        /* No change */
                    }
                    else if (col > indstack[indent])
                    {
                        /* Indent -- always one */
                        // if (indent + 1 >= MAXINDENT) stack will be boomed, but we don't care.
                        pendin++;
                        indstack[++indent] = col;
                    }
                    else /* col < tok->indstack[tok->indent] */
                    {
                        /* Dedent -- any number, must be consistent */
                        while (indent > 0 && col < indstack[indent])
                        {
                            pendin--;
                            indent--;
                        }
                        if (col != indstack[indent])
                        {
                            done = E_DEDENT;
                            cur = inp;
                            return ERRORTOKEN;
                        }
                    }
                }
            }

            start = cur;

            /* Return pending indents/dedents */
            if (pendin != 0)
            {
                if (pendin < 0)
                {
                    pendin++;
                    return DEDENT;
                }
                else
                {
                    pendin--;
                    return INDENT;
                }
            }

            again:
            start = 0;

            /* Skip spaces */
            do
            {
                c = nextc();
            } while (c == ' ' || c == '\t');

            /* Set start of current token */
            start = cur - 1;

            /* Skip comment */
            if (c == '#')
            {
                while (c != EOF && c != '\n')
                {
                    c = nextc();
                }
            }

            /* Check for EOF and errors now */
            if (c == EOF)
            {
                return done == E_EOF ? ENDMARKER : ERRORTOKEN;
            }

            /* Identifier (most frequent token!) */
            if (is_potential_identifier_start(c))
            {
                /* Process the various legal combinations of b"", r"", u"", and f"". */
                bool saw_b = false, saw_r = false, saw_u = false, saw_f = false;
                for (;;)
                {
                    if (!(saw_b || saw_u || saw_f) && (c == 'b' || c == 'B'))
                        saw_b = true;
                    /* Since this is a backwards compatibility support literal we don't
                       want to support it in arbitrary order like byte literals. */
                    else if (!(saw_b || saw_u || saw_r || saw_f)
                             && (c == 'u' || c == 'U'))
                    {
                        saw_u = true;
                    }
                    /* ur"" and ru"" are not supported */
                    else if (!(saw_r || saw_u) && (c == 'r' || c == 'R'))
                    {
                        saw_r = true;
                    }
                    else if (!(saw_f || saw_b || saw_u) && (c == 'f' || c == 'F'))
                    {
                        saw_f = true;
                    }
                    else
                    {
                        break;
                    }
                    c = nextc();
                    if (c == '"' || c == '\'')
                    {
                        goto letter_quote;
                    }
                }
                while (is_potential_identifier_char(c))
                {
                    if (c >= 128)
                    {
                        nonascii = true;
                    }
                    c = nextc();
                }
                backup(c);

                p = _buf.Substring(start, cur - start);

                return NAME;
            }

            /* Newline */
            if (c == '\n')
            {
                atbol = true;
                if (blankline || level > 0)
                {
                    goto nextline;
                }

                p = _buf.Substring(start, cur - start - 1); /* Leave '\n' out of the string */
                cont_line = 0;
                return NEWLINE;
            }

            /* Period or number starting with period? */
            if (c == '.')
            {
                c = nextc();
                if (is_digit(c))
                {
                    if (is_digit(c))
                    {
                        c = decimal_tail();
                        if (c == 0)
                        {
                            return ERRORTOKEN;
                        }
                    }
                    if (c == 'e' || c == 'E')
                    {
                        int e;
                        // exponent:
                        e = c;
                        /* Exponent part */
                        c = nextc();
                        if (c == '+' || c == '-')
                        {
                            c = nextc();
                            if (!is_digit(c))
                            {
                                done = E_TOKEN;
                                backup(c);
                                return ERRORTOKEN;
                            }
                        }
                        else if (!is_digit(c))
                        {
                            backup(c);
                            backup(e);
                            p = _buf.Substring(start, cur - start);
                            return NUMBER;
                        }
                        c = decimal_tail();
                        if (c == 0)
                        {
                            return ERRORTOKEN;
                        }
                    }
                    if (c == 'j' || c == 'J')
                    {
                        /* Imaginary part */
                        // imaginary:
                        c = nextc();
                    }
                }
                else if (c == '.')
                {
                    c = nextc();
                    if (c == '.')   // ...
                    {
                        p = _buf.Substring(start, cur - start);
                        return ELLIPSIS;
                    }
                    else
                    {
                        backup(c);
                    }
                    backup('.');
                }
                else
                {
                    backup(c);
                }
                p = _buf.Substring(start, cur - start);
                return DOT;
            }

            /* Number */
            if (is_digit(c))
            {
                if (c == '0')
                {
                    /* Hex, octal or binary -- maybe. */
                    c = nextc();
                    if (c == 'x' || c == 'X')
                    {
                        /* Hex */
                        c = nextc();
                        do
                        {
                            if (c == '_')
                            {
                                c = nextc();
                            }
                            if (!isxdigit(c))
                            {
                                done = E_TOKEN;
                                backup(c);
                                return ERRORTOKEN;
                            }
                            do
                            {
                                c = nextc();
                            } while (isxdigit(c));
                        } while (c == '_');
                    }
                    else if (c == 'o' || c == 'O')
                    {
                        /* Octal */
                        c = nextc();
                        do
                        {
                            if (c == '_')
                            {
                                c = nextc();
                            }
                            if (c < '0' || c >= '8')
                            {
                                done = E_TOKEN;
                                backup(c);
                                return ERRORTOKEN;
                            }
                            do
                            {
                                c = nextc();
                            } while ('0' <= c && c < '8');
                        } while (c == '_');
                    }
                    else if (c == 'b' || c == 'B')
                    {
                        /* Binary */
                        c = nextc();
                        do
                        {
                            if (c == '_')
                            {
                                c = nextc();
                            }
                            if (c != '0' && c != '1')
                            {
                                done = E_TOKEN;
                                backup(c);
                                return ERRORTOKEN;
                            }
                            do
                            {
                                c = nextc();
                            } while (c == '0' || c == '1');
                        } while (c == '_');
                    }
                    else
                    {
                        int nonzero = 0;
                        /* maybe old-style octal; c is first char of it */
                        /* in any case, allow '0' as a literal */
                        for(;;)
                        {
                            if (c == '_')
                            {
                                c = nextc();
                                if (!is_digit(c))
                                {
                                    done = E_TOKEN;
                                    backup(c);
                                    return ERRORTOKEN;
                                }
                            }
                            if (c != '0')
                            {
                                break;
                            }
                            c = nextc();
                        }
                        if (is_digit(c))
                        {
                            nonzero = 1;
                            c = decimal_tail();
                            if (c == 0)
                            {
                                return ERRORTOKEN;
                            }
                        }
                        if (c == '.')
                        {
                            c = nextc();
                            //goto fraction;
                            if (is_digit(c))
                            {
                                c = decimal_tail();
                                if (c == 0)
                                {
                                    return ERRORTOKEN;
                                }
                            }
                            if (c == 'e' || c == 'E')
                            {
                                int e;
                                // exponent:
                                e = c;
                                /* Exponent part */
                                c = nextc();
                                if (c == '+' || c == '-')
                                {
                                    c = nextc();
                                    if (!is_digit(c))
                                    {
                                        done = E_TOKEN;
                                        backup(c);
                                        return ERRORTOKEN;
                                    }
                                }
                                else if (!is_digit(c))
                                {
                                    backup(c);
                                    backup(e);
                                    p = _buf.Substring(start, cur - start);
                                    return NUMBER;
                                }
                                c = decimal_tail();
                                if (c == 0)
                                {
                                    return ERRORTOKEN;
                                }
                            }
                            if (c == 'j' || c == 'J')
                            {
                                /* Imaginary part */
                                // imaginary:
                                c = nextc();
                            }
                        }
                        else if (c == 'e' || c == 'E')
                        {
                            int e = c;
                            /* Exponent part */
                            c = nextc();
                            if (c == '+' || c == '-')
                            {
                                c = nextc();
                                if (!is_digit(c))
                                {
                                    done = E_TOKEN;
                                    backup(c);
                                    return ERRORTOKEN;
                                }
                            }
                            else if (!is_digit(c))
                            {
                                backup(c);
                                backup(e);
                                p = _buf.Substring(start, cur - start);
                                return NUMBER;
                            }
                            c = decimal_tail();
                            if (c == 0)
                            {
                                return ERRORTOKEN;
                            }
                            if (c == 'j' || c == 'J')
                            {
                                /* Imaginary part */
                                // imaginary:
                                c = nextc();
                            }
                        }
                        else if (c == 'j' || c == 'J')
                        {
                            //goto imaginary;
                            c = nextc();
                        }
                        else if (nonzero != 0)
                        {
                            /* Old-style octal: now disallowed. */
                            done = E_TOKEN;
                            backup(c);
                            return ERRORTOKEN;
                        }
                    }
                }
                else
                {
                    /* Decimal */
                    c = decimal_tail();
                    if (c == 0)
                    {
                        return ERRORTOKEN;
                    }
                    {
                        /* Accept floating point numbers. */
                        if (c == '.')
                        {
                            c = nextc();
                            // fraction:
                            /* Fraction */
                            if (is_digit(c))
                            {
                                c = decimal_tail();
                                if (c == 0)
                                {
                                    return ERRORTOKEN;
                                }
                            }
                        }
                        if (c == 'e' || c == 'E')
                        {
                            int e;
                            // exponent:
                            e = c;
                            /* Exponent part */
                            c = nextc();
                            if (c == '+' || c == '-')
                            {
                                c = nextc();
                                if (!is_digit(c))
                                {
                                    done = E_TOKEN;
                                    backup(c);
                                    return ERRORTOKEN;
                                }
                            }
                            else if (!is_digit(c))
                            {
                                backup(c);
                                backup(e);
                                p = _buf.Substring(start, cur - start);
                                return NUMBER;
                            }
                            c = decimal_tail();
                            if (c == 0)
                            {
                                return ERRORTOKEN;
                            }
                        }
                        if (c == 'j' || c == 'J')
                        {
                            /* Imaginary part */
                            // imaginary:
                            c = nextc();
                        }
                    }
                }
                backup(c);
                p = _buf.Substring(start, cur - start);
                return NUMBER;
            }

            letter_quote:
            /* String */
            if (c == '\'' || c == '"')
            {
                int quote = c;
                int quote_size = 1;             /* 1 or 3 */
                int end_quote_size = 0;

                /* Find the quote size and start of string */
                c = nextc();
                if (c == quote)
                {
                    c = nextc();
                    if (c == quote)
                    {
                        quote_size = 3;
                    }
                    else
                    {
                        end_quote_size = 1;     /* empty string found */
                    }
                }
                if (c != quote)
                {
                    backup(c);
                }

                /* Get rest of string */
                while (end_quote_size != quote_size)
                {
                    c = nextc();
                    if (c == EOF)
                    {
                        if (quote_size == 3)
                        {
                            done = E_EOFS;
                        }
                        else
                        {
                            done = E_EOLS;
                        }
                        cur = inp;
                        return ERRORTOKEN;
                    }
                    if (quote_size == 1 && c == '\n')
                    {
                        done = E_EOLS;
                        cur = inp;
                        return ERRORTOKEN;
                    }
                    if (c == quote)
                    {
                        end_quote_size += 1;
                    }
                    else
                    {
                        end_quote_size = 0;
                        if (c == '\\')
                        {
                            nextc();  /* skip escaped char */
                        }
                    }
                }
                p = _buf.Substring(start, cur - start);
                return STRING;
            }

            /* Line continuation */
            if (c == '\\')
            {
                c = nextc();
                if (c != '\n')
                {
                    done = E_LINECONT;
                    cur = inp;
                    return ERRORTOKEN;
                }
                cont_line = 1;
                goto again; /* Read next line */
            }

            /* Check for two-character token */
            {
                int c2 = nextc();
                int token = _TwoChars(c, c2);
                if (token != OP)
                {
                    int c3 = nextc();
                    int token3 = _ThreeChars(c, c2, c3);
                    if (token3 != OP)
                    {
                        token = token3;
                    }
                    else
                    {
                        backup(c3);
                    }
                    p = _buf.Substring(start, cur - start);
                    return token;
                }
                backup(c2);
            }

            /* Keep track of parentheses nesting level */
            switch (c)
            {
                case '(':
                case '[':
                case '{':
                    level++;
                    break;
                case ')':
                case ']':
                case '}':
                    level--;
                    break;
            }

            /* Punctuation character */
            p = _buf.Substring(start, cur - start);
            return _OneChar(c);
        }

        #region PRIVATE_FUNCTIONS
        bool is_potential_identifier_start(int c) {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_' || (c >= 128);
        }

        bool is_potential_identifier_char(int c) {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || (c >= 128);
        }

        bool is_digit(int c)
        {
            return (c >= '0' && c <= '9');
        }

        bool isxdigit(int c)
        {
            return is_digit(c) || ("ABCDEFabcdef".IndexOf((char)c) != -1);
        }

        int nextc()
        {
            if (cur != inp)
                return _buf[cur++]; /* Fast path */
            if (done != E_OK)
                return EOF;
            while (++end<_buf.Length)
                if(_buf[end] == '\n')
                {
                    if(start == 0) buf = cur;
                    line_start = cur;
                    inp = end;
                    lineno++;
                    return _buf[cur++];
                }
            done = E_EOF;
            return EOF;
        }

        void backup(int c)
        {
            cur--;
        }

        int decimal_tail()
        {
            int c;
            for(;;) {
                do {
                    c = nextc();
                } while (is_digit(c));
                if (c != '_') {
                    break;
                }
                c = nextc();
                if (!is_digit(c)) {
                    done = E_TOKEN;
                    backup(c);
                    return 0;
                }
            }
            return c;
        }

        int _OneChar(int c)
        {
            switch (c)
            {
                case '(': return LPAR;
                case ')': return RPAR;
                case '[': return LSQB;
                case ']': return RSQB;
                case ':': return COLON;
                case ',': return COMMA;
                case ';': return SEMI;
                case '+': return PLUS;
                case '-': return MINUS;
                case '*': return STAR;
                case '/': return SLASH;
                case '|': return VBAR;
                case '&': return AMPER;
                case '<': return LESS;
                case '>': return GREATER;
                case '=': return EQUAL;
                case '.': return DOT;
                case '%': return PERCENT;
                case '{': return LBRACE;
                case '}': return RBRACE;
                case '^': return CIRCUMFLEX;
                case '~': return TILDE;
                case '@': return AT;
                default: return OP;
            }
        }

        int _TwoChars(int c1, int c2)
        {
            switch (c1)
            {
                case '=':
                    switch (c2)
                    {
                        case '=': return EQEQUAL;
                    }
                    break;
                case '!':
                    switch (c2)
                    {
                        case '=': return NOTEQUAL;
                    }
                    break;
                case '<':
                    switch (c2)
                    {
                        case '>': return NOTEQUAL;
                        case '=': return LESSEQUAL;
                        case '<': return LEFTSHIFT;
                    }
                    break;
                case '>':
                    switch (c2)
                    {
                        case '=': return GREATEREQUAL;
                        case '>': return RIGHTSHIFT;
                    }
                    break;
                case '+':
                    switch (c2)
                    {
                        case '=': return PLUSEQUAL;
                    }
                    break;
                case '-':
                    switch (c2)
                    {
                        case '=': return MINEQUAL;
                        case '>': return RARROW;
                    }
                    break;
                case '*':
                    switch (c2)
                    {
                        case '*': return DOUBLESTAR;
                        case '=': return STAREQUAL;
                    }
                    break;
                case '/':
                    switch (c2)
                    {
                        case '/': return DOUBLESLASH;
                        case '=': return SLASHEQUAL;
                    }
                    break;
                case '|':
                    switch (c2)
                    {
                        case '=': return VBAREQUAL;
                    }
                    break;
                case '%':
                    switch (c2)
                    {
                        case '=': return PERCENTEQUAL;
                    }
                    break;
                case '&':
                    switch (c2)
                    {
                        case '=': return AMPEREQUAL;
                    }
                    break;
                case '^':
                    switch (c2)
                    {
                        case '=': return CIRCUMFLEXEQUAL;
                    }
                    break;
                case '@':
                    switch (c2)
                    {
                        case '=': return ATEQUAL;
                    }
                    break;
            }
            return OP;
        }

        int _ThreeChars(int c1, int c2, int c3)
        {
            switch (c1)
            {
                case '<':
                    switch (c2)
                    {
                        case '<':
                            switch (c3)
                            {
                                case '=':
                                    return LEFTSHIFTEQUAL;
                            }
                            break;
                    }
                    break;
                case '>':
                    switch (c2)
                    {
                        case '>':
                            switch (c3)
                            {
                                case '=':
                                    return RIGHTSHIFTEQUAL;
                            }
                            break;
                    }
                    break;
                case '*':
                    switch (c2)
                    {
                        case '*':
                            switch (c3)
                            {
                                case '=':
                                    return DOUBLESTAREQUAL;
                            }
                            break;
                    }
                    break;
                case '/':
                    switch (c2)
                    {
                        case '/':
                            switch (c3)
                            {
                                case '=':
                                    return DOUBLESLASHEQUAL;
                            }
                            break;
                    }
                    break;
                case '.':
                    switch (c2)
                    {
                        case '.':
                            switch (c3)
                            {
                                case '.':
                                    return ELLIPSIS;
                            }
                            break;
                    }
                    break;
            }
            return OP;
        }
        #endregion

        #region SOME_CONSTANTS
        static public readonly string[] _TokenNames = {
    "ENDMARKER",
    "NAME",
    "NUMBER",
    "STRING",
    "NEWLINE",
    "INDENT",
    "DEDENT",
    "LPAR",
    "RPAR",
    "LSQB",
    "RSQB",
    "COLON",
    "COMMA",
    "SEMI",
    "PLUS",
    "MINUS",
    "STAR",
    "SLASH",
    "VBAR",
    "AMPER",
    "LESS",
    "GREATER",
    "EQUAL",
    "DOT",
    "PERCENT",
    "LBRACE",
    "RBRACE",
    "EQEQUAL",
    "NOTEQUAL",
    "LESSEQUAL",
    "GREATEREQUAL",
    "TILDE",
    "CIRCUMFLEX",
    "LEFTSHIFT",
    "RIGHTSHIFT",
    "DOUBLESTAR",
    "PLUSEQUAL",
    "MINEQUAL",
    "STAREQUAL",
    "SLASHEQUAL",
    "PERCENTEQUAL",
    "AMPEREQUAL",
    "VBAREQUAL",
    "CIRCUMFLEXEQUAL",
    "LEFTSHIFTEQUAL",
    "RIGHTSHIFTEQUAL",
    "DOUBLESTAREQUAL",
    "DOUBLESLASH",
    "DOUBLESLASHEQUAL",
    "AT",
    "ATEQUAL",
    "RARROW",
    "ELLIPSIS",
    /* This table must match the const ints in token.h! */
    "OP",
    "<ERRORTOKEN>",
    "COMMENT",
    "NL",
    "ENCODING",
    "<N_TOKENS>"
};
        const int EOF = -1;
        const int ENDMARKER = 0;
        const int NAME = 1;
        const int NUMBER = 2;
        const int STRING = 3;
        const int NEWLINE = 4;
        const int INDENT = 5;
        const int DEDENT = 6;
        const int LPAR = 7;
        const int RPAR = 8;
        const int LSQB = 9;
        const int RSQB = 10;
        const int COLON = 11;
        const int COMMA = 12;
        const int SEMI = 13;
        const int PLUS = 14;
        const int MINUS = 15;
        const int STAR = 16;
        const int SLASH = 17;
        const int VBAR = 18;
        const int AMPER = 19;
        const int LESS = 20;
        const int GREATER = 21;
        const int EQUAL = 22;
        const int DOT = 23;
        const int PERCENT = 24;
        const int LBRACE = 25;
        const int RBRACE = 26;
        const int EQEQUAL = 27;
        const int NOTEQUAL = 28;
        const int LESSEQUAL = 29;
        const int GREATEREQUAL = 30;
        const int TILDE = 31;
        const int CIRCUMFLEX = 32;
        const int LEFTSHIFT = 33;
        const int RIGHTSHIFT = 34;
        const int DOUBLESTAR = 35;
        const int PLUSEQUAL = 36;
        const int MINEQUAL = 37;
        const int STAREQUAL = 38;
        const int SLASHEQUAL = 39;
        const int PERCENTEQUAL = 40;
        const int AMPEREQUAL = 41;
        const int VBAREQUAL = 42;
        const int CIRCUMFLEXEQUAL = 43;
        const int LEFTSHIFTEQUAL = 44;
        const int RIGHTSHIFTEQUAL = 45;
        const int DOUBLESTAREQUAL = 46;
        const int DOUBLESLASH = 47;
        const int DOUBLESLASHEQUAL = 48;
        const int AT = 49;
        const int ATEQUAL = 50;
        const int RARROW = 51;
        const int ELLIPSIS = 52;
        const int OP = 53;
        const int ERRORTOKEN = 54;
        const int COMMENT = 55;
        const int NL = 56;
        const int ENCODING = 57;
        const int N_TOKENS = 58;
        const int E_OK = 10;      /* No error */
        const int E_EOF = 11;      /* End Of File */
        const int E_INTR = 12;      /* Interrupted */
        const int E_TOKEN = 13;      /* Bad token */
        const int E_SYNTAX = 14;      /* Syntax error */
        const int E_NOMEM = 15;      /* Ran out of memory */
        const int E_DONE = 16;      /* Parsing complete */
        const int E_ERROR = 17;      /* Execution error */
        const int E_TABSPACE = 18;      /* Inconsistent mixing of tabs and spaces */
        const int E_OVERFLOW = 19;      /* Node had too many children */
        const int E_TOODEEP = 20;      /* Too many indentation levels */
        const int E_DEDENT = 21;      /* No matching outer block for dedent */
        const int E_DECODE = 22;      /* Error in decoding into Unicode */
        const int E_EOFS = 23;      /* EOF in triple-quoted string */
        const int E_EOLS = 24;      /* EOL in single-quoted string */
        const int E_LINECONT = 25;      /* Unexpected characters after a line continuation */
        const int E_IDENTIFIER = 26;     /* Invalid characters in identifier */
        const int E_BADSINGLE = 27;      /* Ill-formed single statement input */
        #endregion
    }

    class Parser
    {
        stack p_stack;       /* Stack of parser states */
        grammar p_grammar;   /* Grammar to use */
        _node p_tree;        /* Top of parse tree */

        public Parser()
        {

        }

        

        #region SOME_DEFINITIONS
        struct label {
            int lb_type;
            String lb_str;
        };
        const int EMPTY = 0; 
        /* A list of labels */
        class labellist {
            int ll_nlabels;
            label[] ll_label;
        };
        /* An arc from one state to another */
        struct arc {
            short a_lbl;          /* Label of this arc */
            short a_arrow;        /* State where this arc goes to */
        };
        /* A state in a DFA */
        class state {
            int s_narcs;
            arc[] s_arc;         /* Array of arcs */
        };
        /* A DFA */
        class dfa {
            int d_type;        /* Non-terminal this represents */
            String d_name;     /* For printing */
            int d_initial;     /* Initial state */
            int d_nstates;
            state[] d_state;   /* Array of states */
            // bitset d_first;
        };
        /* A grammar */
        class grammar {
            int g_ndfas;
            dfa[] g_dfa;       /* Array of DFAs */
            labellist g_ll;
            int g_start;       /* Start symbol of the grammar */
            int g_accel;       /* Set if accelerators present */
        };
        class _node {
            short n_type;
            String n_str;
            int n_lineno;
            int n_col_offset;
            int n_nchildren;
            _node[] n_child;
        };
        class stackentry {
            int s_state;       /* State in current DFA */
            dfa s_dfa;         /* Current DFA */
            _node s_parent;    /* Where to add next node */
        }
        class stack {
            stackentry s_top;       /* Top entry */
            stackentry[] s_base;    /* Array of stack entries */
        }
        #endregion
    }

    class Compiler
    {
        public static void run(String S, Main M)
        {
            S += '\n';
            Tokenizer t = new Tokenizer(S);
            while(t.ok())
            {
                String p;
                int j = t.get(out p);
                M.printLog(Tokenizer._TokenNames[j] + "  "+ p);
            }
        }

        #region PRIVATE_FUNCTIONS
        #endregion
    }
}

#pragma once

#include <cctype>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <string>
#include <string_view>
#include <unordered_map>
#include <utility>
#include <vector>

namespace mini_json {

class Value {
 public:
  enum class Type {
    kNull,
    kBool,
    kNumber,
    kString,
    kArray,
    kObject
  };

  Value() : type_(Type::kNull) {}

  static Value make_null() { return Value(); }

  static Value make_bool(bool value) {
    Value result;
    result.type_ = Type::kBool;
    result.bool_value_ = value;
    return result;
  }

  static Value make_number(long double value, bool is_integer) {
    Value result;
    result.type_ = Type::kNumber;
    result.number_value_ = value;
    result.number_is_integer_ = is_integer;
    return result;
  }

  static Value make_string(std::string value) {
    Value result;
    result.type_ = Type::kString;
    result.string_value_ = std::move(value);
    return result;
  }

  static Value make_array(std::vector<Value> value) {
    Value result;
    result.type_ = Type::kArray;
    result.array_value_ = std::move(value);
    return result;
  }

  static Value make_object(std::unordered_map<std::string, Value> value) {
    Value result;
    result.type_ = Type::kObject;
    result.object_value_ = std::move(value);
    return result;
  }

  bool is_null() const { return type_ == Type::kNull; }
  bool is_bool() const { return type_ == Type::kBool; }
  bool is_number() const { return type_ == Type::kNumber; }
  bool is_string() const { return type_ == Type::kString; }
  bool is_array() const { return type_ == Type::kArray; }
  bool is_object() const { return type_ == Type::kObject; }

  bool as_bool() const { return bool_value_; }
  long double as_number() const { return number_value_; }
  bool number_is_integer() const { return number_is_integer_; }
  const std::string& as_string() const { return string_value_; }
  const std::vector<Value>& as_array() const { return array_value_; }
  const std::unordered_map<std::string, Value>& as_object() const { return object_value_; }

 private:
  Type type_;
  bool bool_value_ = false;
  long double number_value_ = 0;
  bool number_is_integer_ = false;
  std::string string_value_;
  std::vector<Value> array_value_;
  std::unordered_map<std::string, Value> object_value_;
};

class Parser {
 public:
  explicit Parser(std::string_view input) : input_(input) {}

  bool parse(Value* out_value, std::string* out_error) {
    skip_whitespace();
    if (pos_ >= input_.size()) {
      set_error(out_error, "Expected JSON value.");
      return false;
    }
    Value value = parse_value(out_error);
    if (!out_error->empty()) {
      return false;
    }
    skip_whitespace();
    if (pos_ != input_.size()) {
      set_error(out_error, "Unexpected trailing characters.");
      return false;
    }
    *out_value = std::move(value);
    return true;
  }

 private:
  Value parse_value(std::string* out_error) {
    skip_whitespace();
    if (pos_ >= input_.size()) {
      set_error(out_error, "Unexpected end of input.");
      return Value::make_null();
    }

    char ch = input_[pos_];
    if (ch == 'n') {
      return parse_literal("null", Value::make_null(), out_error);
    }
    if (ch == 't') {
      return parse_literal("true", Value::make_bool(true), out_error);
    }
    if (ch == 'f') {
      return parse_literal("false", Value::make_bool(false), out_error);
    }
    if (ch == '"') {
      return Value::make_string(parse_string(out_error));
    }
    if (ch == '[') {
      return parse_array(out_error);
    }
    if (ch == '{') {
      return parse_object(out_error);
    }
    if (ch == '-' || std::isdigit(static_cast<unsigned char>(ch))) {
      return parse_number(out_error);
    }

    set_error(out_error, "Invalid JSON value.");
    return Value::make_null();
  }

  Value parse_literal(std::string_view literal, Value value, std::string* out_error) {
    if (input_.substr(pos_, literal.size()) == literal) {
      pos_ += literal.size();
      return value;
    }
    set_error(out_error, "Invalid literal.");
    return Value::make_null();
  }

  Value parse_array(std::string* out_error) {
    if (!consume('[')) {
      set_error(out_error, "Expected '['.");
      return Value::make_null();
    }

    skip_whitespace();
    std::vector<Value> values;
    if (consume(']')) {
      return Value::make_array(std::move(values));
    }

    while (pos_ < input_.size()) {
      Value value = parse_value(out_error);
      if (!out_error->empty()) {
        return Value::make_null();
      }
      values.push_back(std::move(value));
      skip_whitespace();
      if (consume(']')) {
        return Value::make_array(std::move(values));
      }
      if (!consume(',')) {
        set_error(out_error, "Expected ',' in array.");
        return Value::make_null();
      }
      skip_whitespace();
    }

    set_error(out_error, "Unterminated array.");
    return Value::make_null();
  }

  Value parse_object(std::string* out_error) {
    if (!consume('{')) {
      set_error(out_error, "Expected '{'.");
      return Value::make_null();
    }

    skip_whitespace();
    std::unordered_map<std::string, Value> values;
    if (consume('}')) {
      return Value::make_object(std::move(values));
    }

    while (pos_ < input_.size()) {
      if (input_[pos_] != '"') {
        set_error(out_error, "Expected object key string.");
        return Value::make_null();
      }
      std::string key = parse_string(out_error);
      if (!out_error->empty()) {
        return Value::make_null();
      }
      skip_whitespace();
      if (!consume(':')) {
        set_error(out_error, "Expected ':' after object key.");
        return Value::make_null();
      }
      Value value = parse_value(out_error);
      if (!out_error->empty()) {
        return Value::make_null();
      }
      values.emplace(std::move(key), std::move(value));
      skip_whitespace();
      if (consume('}')) {
        return Value::make_object(std::move(values));
      }
      if (!consume(',')) {
        set_error(out_error, "Expected ',' in object.");
        return Value::make_null();
      }
      skip_whitespace();
    }

    set_error(out_error, "Unterminated object.");
    return Value::make_null();
  }

  Value parse_number(std::string* out_error) {
    const size_t start = pos_;
    bool has_fraction = false;
    bool has_exponent = false;

    if (consume('-')) {
      if (pos_ >= input_.size()) {
        set_error(out_error, "Invalid number.");
        return Value::make_null();
      }
    }

    if (consume('0')) {
      // Leading zero allowed only if no more integer digits.
    } else if (std::isdigit(static_cast<unsigned char>(peek()))) {
      while (std::isdigit(static_cast<unsigned char>(peek()))) {
        ++pos_;
      }
    } else {
      set_error(out_error, "Invalid number.");
      return Value::make_null();
    }

    if (consume('.')) {
      has_fraction = true;
      if (!std::isdigit(static_cast<unsigned char>(peek()))) {
        set_error(out_error, "Invalid fractional number.");
        return Value::make_null();
      }
      while (std::isdigit(static_cast<unsigned char>(peek()))) {
        ++pos_;
      }
    }

    if (peek() == 'e' || peek() == 'E') {
      has_exponent = true;
      ++pos_;
      if (peek() == '+' || peek() == '-') {
        ++pos_;
      }
      if (!std::isdigit(static_cast<unsigned char>(peek()))) {
        set_error(out_error, "Invalid exponent.");
        return Value::make_null();
      }
      while (std::isdigit(static_cast<unsigned char>(peek()))) {
        ++pos_;
      }
    }

    std::string number_text(input_.substr(start, pos_ - start));
    char* end_ptr = nullptr;
    long double value = std::strtold(number_text.c_str(), &end_ptr);
    if (!end_ptr || *end_ptr != '\0') {
      set_error(out_error, "Invalid number.");
      return Value::make_null();
    }

    return Value::make_number(value, !(has_fraction || has_exponent));
  }

  std::string parse_string(std::string* out_error) {
    if (!consume('"')) {
      set_error(out_error, "Expected string.");
      return {};
    }

    std::string result;
    while (pos_ < input_.size()) {
      char ch = input_[pos_++];
      if (ch == '"') {
        return result;
      }
      if (static_cast<unsigned char>(ch) < 0x20) {
        set_error(out_error, "Control character in string.");
        return {};
      }
      if (ch == '\\') {
        if (pos_ >= input_.size()) {
          set_error(out_error, "Unterminated escape sequence.");
          return {};
        }
        char esc = input_[pos_++];
        switch (esc) {
          case '"':
          case '\\':
          case '/':
            result.push_back(esc);
            break;
          case 'b':
            result.push_back('\b');
            break;
          case 'f':
            result.push_back('\f');
            break;
          case 'n':
            result.push_back('\n');
            break;
          case 'r':
            result.push_back('\r');
            break;
          case 't':
            result.push_back('\t');
            break;
          case 'u':
            if (!parse_unicode_escape(&result, out_error)) {
              return {};
            }
            break;
          default:
            set_error(out_error, "Invalid escape sequence.");
            return {};
        }
      } else {
        result.push_back(ch);
      }
    }

    set_error(out_error, "Unterminated string.");
    return {};
  }

  bool parse_unicode_escape(std::string* output, std::string* out_error) {
    if (pos_ + 4 > input_.size()) {
      set_error(out_error, "Invalid unicode escape.");
      return false;
    }
    uint32_t codepoint = 0;
    for (int i = 0; i < 4; ++i) {
      char ch = input_[pos_++];
      codepoint <<= 4;
      if (ch >= '0' && ch <= '9') {
        codepoint |= static_cast<uint32_t>(ch - '0');
      } else if (ch >= 'a' && ch <= 'f') {
        codepoint |= static_cast<uint32_t>(10 + (ch - 'a'));
      } else if (ch >= 'A' && ch <= 'F') {
        codepoint |= static_cast<uint32_t>(10 + (ch - 'A'));
      } else {
        set_error(out_error, "Invalid unicode escape.");
        return false;
      }
    }

    append_utf8(codepoint, output);
    return true;
  }

  void append_utf8(uint32_t codepoint, std::string* output) {
    if (codepoint <= 0x7F) {
      output->push_back(static_cast<char>(codepoint));
    } else if (codepoint <= 0x7FF) {
      output->push_back(static_cast<char>(0xC0 | (codepoint >> 6)));
      output->push_back(static_cast<char>(0x80 | (codepoint & 0x3F)));
    } else if (codepoint <= 0xFFFF) {
      output->push_back(static_cast<char>(0xE0 | (codepoint >> 12)));
      output->push_back(static_cast<char>(0x80 | ((codepoint >> 6) & 0x3F)));
      output->push_back(static_cast<char>(0x80 | (codepoint & 0x3F)));
    } else if (codepoint <= 0x10FFFF) {
      output->push_back(static_cast<char>(0xF0 | (codepoint >> 18)));
      output->push_back(static_cast<char>(0x80 | ((codepoint >> 12) & 0x3F)));
      output->push_back(static_cast<char>(0x80 | ((codepoint >> 6) & 0x3F)));
      output->push_back(static_cast<char>(0x80 | (codepoint & 0x3F)));
    }
  }

  void skip_whitespace() {
    while (pos_ < input_.size() && std::isspace(static_cast<unsigned char>(input_[pos_]))) {
      ++pos_;
    }
  }

  bool consume(char expected) {
    if (pos_ < input_.size() && input_[pos_] == expected) {
      ++pos_;
      return true;
    }
    return false;
  }

  char peek() const {
    if (pos_ < input_.size()) {
      return input_[pos_];
    }
    return '\0';
  }

  void set_error(std::string* out_error, const char* message) {
    if (out_error && out_error->empty()) {
      *out_error = message;
    }
  }

  std::string_view input_;
  size_t pos_ = 0;
};

inline bool parse(std::string_view input, Value* out_value, std::string* out_error) {
  std::string fallback_error;
  std::string* error_ptr = out_error ? out_error : &fallback_error;
  error_ptr->clear();
  Parser parser(input);
  return parser.parse(out_value, error_ptr);
}

}  // namespace mini_json
